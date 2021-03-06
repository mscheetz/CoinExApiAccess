﻿using CoinExApiAccess.Core;
using CoinExApiAccess.Data.Interface;
using CoinExApiAccess.Entities;
using DateTimeHelpers;
using FileRepository;
using RESTApiAccess;
using RESTApiAccess.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinExApiAccess.Data
{
    public class CoinExRepository : ICoinExRepository
    {
        private Security security;
        private IRESTRepository _restRepo;
        private DateTimeHelper _dtHelper;
        private ApiInformation _apiInfo = null;
        private Helper _helper;
        private string baseUrl;
        private Dictionary<int, string> errorCodes;

        /// <summary>
        /// Constructor for non-signed endpoints
        /// </summary>
        public CoinExRepository()
        {
            LoadRepository();
        }

        /// <summary>
        /// Constructor for signed endpoints
        /// </summary>
        /// <param name="apiKey">Api key</param>
        /// <param name="apiSecret">Api secret</param>
        public CoinExRepository(string apiKey, string apiSecret)
        {
            _apiInfo = new ApiInformation
            {
                apiKey = apiKey,
                apiSecret = apiSecret
            };
            LoadRepository();
        }

        /// <summary>
        /// Constructor for signed endpoints
        /// </summary>
        /// <param name="configPath">String of path to configuration file</param>
        public CoinExRepository(string configPath)
        {
            IFileRepository _fileRepo = new FileRepository.FileRepository();

            if (_fileRepo.FileExists(configPath))
            {
                _apiInfo = _fileRepo.GetDataFromFile<ApiInformation>(configPath);
                LoadRepository();
            }
            else
            {
                throw new Exception("Config file not found");
            }
        }

        /// <summary>
        /// Load repository
        /// </summary>
        /// <param name="key">Api key value (default = "")</param>
        /// <param name="secret">Api secret value (default = "")</param>
        private void LoadRepository(string key = "", string secret = "")
        {
            security = new Security();
            _restRepo = new RESTRepository();
            baseUrl = "https://api.coinex.com/v1";
            _dtHelper = new DateTimeHelper();
            _helper = new Helper();
        }

        /// <summary>
        /// Check if the Exchange Repository is ready for trading
        /// </summary>
        /// <returns>Boolean of validation</returns>
        public bool ValidateExchangeConfigured()
        {
            var ready = _apiInfo == null || string.IsNullOrEmpty(_apiInfo.apiKey) ? false : true;
            if (!ready)
                return false;

            return string.IsNullOrEmpty(_apiInfo.apiSecret) ? false : true;
        }

        /// <summary>
        /// Get Trading Pairs
        /// </summary>
        /// <returns>Array of string of trading pairs</returns>
        public async Task<string[]> GetMarketList()
        {
            var endpoint = "/market/list";
            var url = baseUrl + endpoint;

            var response = await _restRepo.GetApiStream<ResponseMessage<string[]>>(url);

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get Ticker for a trading pair
        /// </summary>
        /// <param name="pair">String of trading pair</param>
        /// <returns>TickerData object</returns>
        public async Task<TickerData> GetTicker(string pair)
        {
            var endpoint = $"/market/ticker?market={pair}";
            var url = baseUrl + endpoint;

            var response = await _restRepo.GetApiStream<ResponseMessage<TickerData>>(url);

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get Market depth for a trading pair
        /// </summary>
        /// <param name="pair">String of trading pair</param>
        /// <param name="merge">Decimal places</param>
        /// <param name="limit">Return amount</param>
        /// <returns>MarketDepth object</returns>
        public async Task<MarketDepth> GetMarketDepth(string pair, Merge merge = Merge.Zero, Limit limit = Limit.Twenty)
        {
            var mergeValue = (int)Convert.ChangeType(merge, merge.GetTypeCode());
            var limitValue = (int)Convert.ChangeType(limit, limit.GetTypeCode());
            var decimalPlaces = mergeValue == 0 ? 0 : _helper.DecimalValueAtPrecision(mergeValue);

            var endpoint = $"/market/depth?market={pair}&merge={mergeValue}&limit={limitValue}";
            var url = baseUrl + endpoint;

            var response = await _restRepo.GetApiStream<ResponseMessage<MarketDepth>>(url);

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get Transaction data
        /// </summary>
        /// <param name="pair">String of trading pair</param>
        /// <param name="id">Transaction history id</param>
        /// <returns>Transaction object</returns>
        public async Task<Transaction> GetTransactionData(string pair, int id = 0)
        {
            var endpoint = $"/market/ticker?market={pair}&last_id={id}";
            var url = baseUrl + endpoint;

            var response = await _restRepo.GetApiStream<ResponseMessage<Transaction>>(url);

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get K-Line data
        /// </summary>
        /// <param name="pair">String of trading pair</param>
        /// <param name="interval">Time interval for KLines</param>
        /// <param name="limit">Number of KLines to return, (default = 100, max = 1000)</param>
        /// <returns>Array of KLine objects</returns>
        public async Task<KLine[]> GetKLine(string pair, Interval interval, int limit = 100)
        {
            limit = limit > 1000 ? 1000 : limit;
            var intervalString = _helper.IntervalToString(interval);
            var endpoint = $"/market/kline?market={pair}&limit={limit}&type={intervalString}";
            var url = baseUrl + endpoint;

            var response = await _restRepo.GetApiStream<ResponseMessage<KLine[]>>(url);

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get account balance
        /// </summary>
        /// <returns>Dictionary of Coin / value pairs</returns>
        public async Task<Dictionary<string, Asset>> GetBalance()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", _apiInfo.apiKey);
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds().ToString());

            var endpoint = $"/balance/info";
            var url = CreateUrl(endpoint, parameters);
            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<Dictionary<string, Asset>>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get all Withdrawals from account
        /// </summary>
        /// <returns>Array of Withdrawal objects</returns>
        public async Task<Withdrawal[]> GetWithdrawals()
        {
            return await OnGetWithdrawals();
        }

        /// <summary>
        /// Get Withdrawals from account by page number
        /// </summary>
        /// <param name="page">Page number to return (default = 1)</param>
        /// <returns>Array of Withdrawal objects</returns>
        public async Task<Withdrawal[]> GetWithdrawals(int page = 1)
        {
            return await OnGetWithdrawals("", page);
        }

        /// <summary>
        /// Get Withdrawals from account
        /// </summary>
        /// <param name="coin">Coin to return (default = "")</param>
        /// <param name="withdrawlId">Id of withdrawal to start listing (optional)</param>
        /// <param name="page">Page number to return (default = 1)</param>
        /// <param name="limit">Number of records to return (default = 100)</param>
        /// <returns>Array of Withdrawal objects</returns>
        public async Task<Withdrawal[]> GetWithdrawals(string coin = "", int withdrawlId = 0, int page = 1, int limit = 100)
        {
            return await OnGetWithdrawals(coin, withdrawlId, page, limit);
        }

        /// <summary>
        /// Get Withdrawals from account
        /// </summary>
        /// <param name="coin">Coin to return (default = "")</param>
        /// <param name="withdrawlId">Id of withdrawal to start listing (optional)</param>
        /// <param name="limit">Number of records to return (default = 100)</param>
        /// <returns>Array of Withdrawal objects</returns>
        private async Task<Withdrawal[]> OnGetWithdrawals(string coin = "", int withdrawlId = 0, int page = 1, int limit = 100)
        {
            limit = limit > 100 ? 100 : limit;
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", _apiInfo.apiKey);
            if (!string.IsNullOrEmpty(coin))
            {
                parameters.Add("coin_type", coin);
            }
            if (withdrawlId > 0)
            {
                parameters.Add("coin_withdraw_id", withdrawlId);
            }
            if (limit < 100)
            {
                parameters.Add("limit", limit);
            }
            if (page > 1)
            {
                parameters.Add("page", page);
            }
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds().ToString());

            var endpoint = $"/balance/coin/withdraw";
            var url = CreateUrl(endpoint, parameters);
            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<Withdrawal[]>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Submit a withdrawal
        /// </summary>
        /// <param name="coin">Coin to send</param>
        /// <param name="address">Address to send to</param>
        /// <param name="amount">Amount to send</param>
        /// <returns>Withdrawal object</returns>
        public async Task<Withdrawal> SubmitWithdrawal(string coin, string address, decimal amount)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", _apiInfo.apiKey);
            parameters.Add("actual_amount", amount.ToString());
            parameters.Add("coin_address", address);
            parameters.Add("coin_type", coin);
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds().ToString());

            var endpoint = $"/balance/coin/withdraw";
            var url = CreateUrl(endpoint);
            var signature = GetSignature(parameters);

            var response = await _restRepo.PostApi<ResponseMessage<Withdrawal>, Dictionary<string, object>>(url, parameters, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Cancel a withdrawal
        /// </summary>
        /// <param name="id">Withdrawal request Id</param>
        /// <returns>Boolean when complete</returns>
        public async Task<bool> CancelWithdrawal(long id)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", _apiInfo.apiKey);
            parameters.Add("coin_withdraw_id", id);
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds().ToString());

            var endpoint = $"/balance/coin/withdraw";
            var url = CreateUrl(endpoint, parameters);
            var signature = GetSignature(parameters);

            var response = await _restRepo.DeleteApi<ResponseMessage<object>>(url, GetRequestHeaders(signature));

            ResponseHandler(response);

            return true;
        }

        /// <summary>
        /// Post Limit Order
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="type">Trade type</param>
        /// <param name="amount">Trade amount</param>
        /// <param name="price">Trade price</param>
        /// <returns>Order object</returns>
        public async Task<Order> LimitOrder(string pair, OrderType type, decimal amount, decimal price)
        {
            var endpoint = $"/order/limit";

            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("amount", $"{amount}");
            parameters.Add("market", $"{pair}");
            parameters.Add("price", $"{price}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());
            parameters.Add("type", $"{type.ToString().ToLower()}");

            var url = CreateUrl(endpoint);
            var signature = GetSignature(parameters);

            var response = await _restRepo.PostApi<ResponseMessage<Order>, Dictionary<string, object>>(url, parameters, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Post Market Order
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="type">Trade type</param>
        /// <param name="amount">Trade amount</param>
        /// <returns>Order object</returns>
        public async Task<Order> MarketOrder(string pair, OrderType type, decimal amount)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("amount", $"{amount}");
            parameters.Add("market", $"{pair}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());
            parameters.Add("type", $"{type.ToString().ToLower()}");

            var endpoint = $"/order/market";
            var url = CreateUrl(endpoint);

            var signature = GetSignature(parameters);

            var response = await _restRepo.PostApi<ResponseMessage<Order>, Dictionary<string, object>>(url, parameters, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Post IOC (Immediate-or-Cancel) Order
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="type">Trade type</param>
        /// <param name="amount">Trade amount</param>
        /// <param name="price">Trade price</param>
        /// <returns>Order object</returns>
        public async Task<Order> IOCOrder(string pair, OrderType type, decimal amount, decimal price)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("amount", $"{amount}");
            parameters.Add("market", $"{pair}");
            parameters.Add("price", $"{price}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());
            parameters.Add("type", $"{type.ToString().ToLower()}");

            var endpoint = $"/order/ioc";
            var url = CreateUrl(endpoint);

            var signature = GetSignature(parameters);

            var response = await _restRepo.PostApi<ResponseMessage<Order>, Dictionary<string, object>>(url, parameters, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get Open Orders
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="page">Page number (default = 1)</param>
        /// <param name="limit">Number of order to return (default = 100, max = 100)</param>
        /// <returns>PagedResponse with OpenOrder array</returns>
        public async Task<PagedResponse<OpenOrder[]>> GetOpenOrders(string pair, int page = 1, int limit = 100)
        {
            limit = limit > 100 ? 100 : limit;
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("limit", $"{limit}");
            parameters.Add("market", $"{pair}");
            parameters.Add("page", $"{page}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());

            var endpoint = $"/order/pending";
            var url = CreateUrl(endpoint, parameters);

            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<PagedResponse<OpenOrder[]>>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get an order
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="id">Order Number</param>
        /// <returns>Order object</returns>
        public async Task<Order> GetOrder(string pair, int id)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("id", $"{id}");
            parameters.Add("market", $"{pair}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());

            var endpoint = $"/order/status";
            var url = CreateUrl(endpoint, parameters);

            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<Order>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get completed orders
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="page">Page number (default = 1)</param>
        /// <param name="limit">Number of order to return (default = 100, max = 100)</param>
        /// <returns>PagedResponse with Order array</returns>
        public async Task<PagedResponse<Order[]>> GetOrders(string pair, int page = 1, int limit = 100)
        {
            limit = limit > 100 ? 100 : limit;
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("limit", $"{limit}");
            parameters.Add("market", $"{pair}");
            parameters.Add("page", $"{page}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());

            var endpoint = $"/order/finished";
            var url = CreateUrl(endpoint, parameters);

            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<PagedResponse<Order[]>>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get user deals
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="page">Page number (default = 1)</param>
        /// <param name="limit">Number of order to return (default = 100, max = 100)</param>
        /// <returns>PagedResponse with Deal array</returns>
        public async Task<PagedResponse<Deal[]>> GetUserDeals(string pair, int page = 1, int limit = 100)
        {
            limit = limit > 100 ? 100 : limit;
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("limit", $"{limit}");
            parameters.Add("market", $"{pair}");
            parameters.Add("page", $"{page}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());

            var endpoint = $"/order/finished";
            var url = CreateUrl(endpoint, parameters);

            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<PagedResponse<Deal[]>>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Cancel an order
        /// </summary>
        /// <param name="pair">Trading pair</param>
        /// <param name="id">Order Number</param>
        /// <returns>Order object</returns>
        public async Task<Order> CancelOrder(string pair, int id)
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("id", $"{id}");
            parameters.Add("market", $"{pair}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());

            var endpoint = $"/order/status";
            var url = CreateUrl(endpoint, parameters);

            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<Order>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Get mining difficulty
        /// </summary>
        /// <returns>Current MiningDifficulty</returns>
        public async Task<MiningDifficulty> GetMiningDifficulty()
        {
            var parameters = new Dictionary<string, object>();
            parameters.Add("access_id", $"{_apiInfo.apiKey}");
            parameters.Add("tonce", _dtHelper.UTCtoUnixTimeMilliseconds());

            var endpoint = $"/order/mining/difficulty";
            var url = CreateUrl(endpoint);

            var signature = GetSignature(parameters);

            var response = await _restRepo.GetApiStream<ResponseMessage<MiningDifficulty>>(url, GetRequestHeaders(signature));

            return ResponseHandler(response);
        }

        /// <summary>
        /// Handle null responses or error codes from responses
        /// </summary>
        /// <typeparam name="T">Object type to return</typeparam>
        /// <param name="response">ResponseMessage to process</param>
        /// <returns>data from response or exception thrown</returns>
        private T ResponseHandler<T>(ResponseMessage<T> response)
        {
            if (response == null)
            {
                throw new Exception("Something went wrong...");
            }
            else if (response.code != 0)
            {
                throw new Exception(response.message);
            }
            else
            {
                return response.data;
            }
        }

        /// <summary>
        /// Get Request Headers
        /// </summary>
        /// <returns>Dictionary of header values</returns>
        private Dictionary<string, string> GetRequestHeaders(string signature)
        {
            var headers = new Dictionary<string, string>();
            headers.Add("authorization", signature);
            headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/39.0.2171.71 Safari/537.36");

            return headers;
        }

        /// <summary>
        /// Get signature of message
        /// </summary>
        /// <param name="queryString">string of querystring values</param>
        /// <returns>String of message signature</returns>
        private string GetSignature(string queryString)
        {
            queryString += $"&secret_key={_apiInfo.apiSecret}";

            var hmac = security.GetHMACSignature(queryString);

            return hmac;
        }

        /// <summary>
        /// Get signature of message
        /// </summary>
        /// <param name="queryString">String[] of querystring values</param>
        /// <returns>String of message signature</returns>
        private string GetSignature(List<string> queryString)
        {
            var qsValues = string.Empty;

            if (queryString != null)
            {
                queryString = queryString.OrderBy(q => q).ToList();
                for (int i = 0; i < queryString.Count; i++)
                {
                    qsValues += qsValues != string.Empty ? "&" : "";
                    qsValues += queryString[i];
                }
            }

            return GetSignature(qsValues);
        }

        /// <summary>
        /// Get signature of message
        /// </summary>
        /// <param name="parameters">Dictionary of request parameters</param>
        /// <returns>String of message signature</returns>
        private string GetSignature(Dictionary<string, object> parameters)
        {
            var qsValues = StringifyDictionary(parameters);

            return GetSignature(qsValues);
        }

        /// <summary>
        /// Create a CoinEx url
        /// </summary>
        /// <param name="apiPath">String of path to endpoint</param>
        /// <returns>String of url</returns>
        private string CreateUrl(string apiPath)
        {
            return baseUrl + $"{apiPath}";
        }

        /// <summary>
        /// Create a CoinEx url
        /// </summary>
        /// <param name="apiPath">String of path to endpoint</param>
        /// <param name="parameters">String[] of querystring values</param>
        /// <returns>String of url</returns>
        private string CreateUrl(string apiPath, Dictionary<string, object> parameters = null)
        {
            var qsValues = StringifyDictionary(parameters);
            var url = baseUrl + $"{apiPath}";

            if (qsValues != string.Empty)
                url += "?" + qsValues;

            return url;
        }

        /// <summary>
        /// Create a CoinEx url
        /// </summary>
        /// <param name="apiPath">String of path to endpoint</param>
        /// <param name="parameters">String[] of querystring values</param>
        /// <returns>String of url</returns>
        private string CreateUrl(string apiPath, List<string> parameters = null)
        {
            var qsValues = StringifyList(parameters);
            var url = baseUrl + $"{apiPath}";

            if (qsValues != string.Empty)
                url += "?" + qsValues;

            return url;
        }

        private string StringifyDictionary(Dictionary<string, object> parameters)
        {
            var qsValues = string.Empty;

            if (parameters != null)
            {
                qsValues = string.Join("&", parameters.Select(p => p.Key + "=" + p.Value));
            }

            return qsValues;
        }

        private string StringifyList(List<string> parameters)
        {
            var qsValues = string.Empty;

            if (parameters != null)
            {
                parameters = parameters.OrderBy(q => q).ToList();
                for (int i = 0; i < parameters.Count; i++)
                {
                    qsValues += qsValues != string.Empty ? "&" : "";
                    qsValues += parameters[i];
                }
            }

            return qsValues;
        }
    }
}
