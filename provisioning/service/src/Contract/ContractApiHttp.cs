﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.Devices.Common;
using Microsoft.Azure.Devices.Common.Service.Auth;
using Newtonsoft.Json;
#if !WINDOWS_UWP && !NETSTANDARD1_3 && !NETSTANDARD2_0
    using System.Net.Http.Formatting;
#endif

namespace Microsoft.Azure.Devices.Provisioning.Service
{
    internal sealed class ContractApiHttp : IContractApiHttp
    {
#if !WINDOWS_UWP && !NETSTANDARD1_3 && !NETSTANDARD2_0
        private static readonly JsonMediaTypeFormatter _jsonFormatter = new JsonMediaTypeFormatter();
#endif
        private readonly Uri _baseAddress;
        private readonly IAuthorizationHeaderProvider _authenticationHeaderProvider;
        private readonly IReadOnlyDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> _defaultErrorMapping;
        private HttpClient _httpClientObj;
        private HttpClient _httpClientObjWithPerRequestTimeout;
        private bool _isDisposed;
        private readonly TimeSpan _defaultOperationTimeout;

        public ContractApiHttp(
            Uri baseAddress,
            IAuthorizationHeaderProvider authenticationHeaderProvider,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> defaultErrorMapping,
            TimeSpan timeout,
            Action<HttpClient> preRequestActionForAllRequests)
        {
            _baseAddress = baseAddress;
            _authenticationHeaderProvider = authenticationHeaderProvider;
            _defaultErrorMapping =
                new ReadOnlyDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>>(defaultErrorMapping);
            _defaultOperationTimeout = timeout;

            _httpClientObj = new HttpClient();
            _httpClientObj.BaseAddress = _baseAddress;
            _httpClientObj.Timeout = timeout;
            _httpClientObj.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(CommonConstants.MediaTypeForDeviceManagementApis));
            _httpClientObj.DefaultRequestHeaders.ExpectContinue = false;

            _httpClientObjWithPerRequestTimeout = new HttpClient();
            _httpClientObjWithPerRequestTimeout.BaseAddress = _baseAddress;
            _httpClientObjWithPerRequestTimeout.Timeout = Timeout.InfiniteTimeSpan;
            _httpClientObjWithPerRequestTimeout.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(CommonConstants.MediaTypeForDeviceManagementApis));
            _httpClientObjWithPerRequestTimeout.DefaultRequestHeaders.ExpectContinue = false;

            if (preRequestActionForAllRequests != null)
            {
                preRequestActionForAllRequests(_httpClientObj);
                preRequestActionForAllRequests(_httpClientObjWithPerRequestTimeout);
            }
        }

        public async Task<T> GetAsync<T>(
            Uri requestUri,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            IDictionary<string, string> customHeaders,
            CancellationToken cancellationToken)
        {
            T result = default(T);

            await ExecuteAsync(
                _httpClientObj,
                HttpMethod.Get,
                new Uri(_baseAddress, requestUri),
                (requestMsg, token) => AddCustomHeaders(requestMsg, customHeaders),
                message => !(message.IsSuccessStatusCode || message.StatusCode == HttpStatusCode.NotFound),
                async (message, token) => result = message.StatusCode == HttpStatusCode.NotFound ? (default(T)) : await ReadResponseMessageAsync<T>(message, token),
                errorMappingOverrides,
                cancellationToken);

            return result;
        }

        public async Task<T> PutAsync<T>(
            Uri requestUri,
            T entity,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            IDictionary<string, string> customHeaders,
            CancellationToken cancellationToken) where T : IETagHolder
        {
            T result = default(T);

            await ExecuteAsync(
                    HttpMethod.Put,
                    new Uri(_baseAddress, requestUri),
                    (requestMsg, token) =>
                    {
                        InsertIfMatch(requestMsg, entity.ETag);
                        AddCustomHeaders(requestMsg, customHeaders);
#if WINDOWS_UWP || NETSTANDARD1_3 || NETSTANDARD2_0
                        var str = Newtonsoft.Json.JsonConvert.SerializeObject(entity);
                        requestMsg.Content = new StringContent(str, System.Text.Encoding.UTF8, CommonConstants.MediaTypeForDeviceManagementApis);
#else
                        requestMsg.Content = new ObjectContent<T>(entity, _jsonFormatter);
#endif
                        return Task.FromResult(0);
                    },
                    async (httpClient, token) => result = await ReadResponseMessageAsync<T>(httpClient, token),
                    errorMappingOverrides,
                    cancellationToken);

            return result;
        }

        private static async Task<T> ReadResponseMessageAsync<T>(HttpResponseMessage message, CancellationToken token)
        {
            if (typeof(T) == typeof(HttpResponseMessage))
            {
                return (T)(object)message;
            }

#if WINDOWS_UWP || NETSTANDARD1_3 || NETSTANDARD2_0
            var str = await message.Content.ReadAsStringAsync();
            T entity = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(str);
#else
            T entity = await message.Content.ReadAsAsync<T>(token);
#endif
            // Etag in the header is considered authoritative
            var eTagHolder = entity as IETagHolder;
            if (eTagHolder != null)
            {
                if (message.Headers.ETag != null && !string.IsNullOrWhiteSpace(message.Headers.ETag.Tag))
                {
                    // RDBug 3429280:Make the version field of Device object internal
                    eTagHolder.ETag = message.Headers.ETag.Tag;
                }
            }

            return entity;
        }

        private static Task AddCustomHeaders(HttpRequestMessage requestMessage, IDictionary<string, string> customHeaders)
        {
            if (customHeaders != null)
            {
                foreach (var header in customHeaders)
                {
                    requestMessage.Headers.Add(header.Key, header.Value);
                }
            }

            return Task.FromResult(0);
        }

        private static void InsertIfMatch(HttpRequestMessage requestMessage, string ifMatch)
        {
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                return;
            }

            if (!ifMatch.StartsWith("\"", StringComparison.OrdinalIgnoreCase))
            {
                ifMatch = "\"" + ifMatch;
            }

            if (!ifMatch.EndsWith("\"", StringComparison.OrdinalIgnoreCase))
            {
                ifMatch = ifMatch + "\"";
            }

            requestMessage.Headers.IfMatch.Add(new EntityTagHeaderValue(ifMatch));
        }

        private IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> MergeErrorMapping(
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides)
        {
            var mergedMapping = _defaultErrorMapping.ToDictionary(mapping => mapping.Key, mapping => mapping.Value);

            if (errorMappingOverrides != null)
            {
                foreach (var @override in errorMappingOverrides)
                {
                    mergedMapping[@override.Key] = @override.Value;
                }
            }

            return mergedMapping;
        }

        public async Task<T2> PostAsync<T1, T2>(
            Uri requestUri,
            T1 entity,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            IDictionary<string, string> customHeaders,
            CancellationToken cancellationToken)
        {
            T2 result = default(T2);
            await PostAsyncHelper(
                requestUri,
                entity,
                TimeSpan.Zero,
                errorMappingOverrides,
                customHeaders,
                null,
                null,
                async (message, token) => result = await ReadResponseMessageAsync<T2>(message, token),
                cancellationToken);

            return result;
        }

        private Task PostAsyncHelper<T1>(
            Uri requestUri,
            T1 entity,
            TimeSpan operationTimeout,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            IDictionary<string, string> customHeaders,
            MediaTypeHeaderValue customContentType,
            ICollection<string> customContentEncoding,
            Func<HttpResponseMessage, CancellationToken, Task> processResponseMessageAsync,
            CancellationToken cancellationToken)
        {
            Func<HttpRequestMessage, CancellationToken, Task> modifyRequestMessageFunc = (requestMsg, token) =>
            {
                AddCustomHeaders(requestMsg, customHeaders);
                if (entity != null)
                {
                    if (typeof(T1) == typeof(byte[]))
                    {
                        requestMsg.Content = new ByteArrayContent((byte[])(object)entity);
                    }
                    else if (typeof(T1) == typeof(string))
                    {
                        // only used to send batched messages on Http runtime
                        requestMsg.Content = new StringContent((string)(object)entity);
                        requestMsg.Content.Headers.ContentType = new MediaTypeHeaderValue(CommonConstants.BatchedMessageContentType);
                    }
                    else
                    {
                            var str = Newtonsoft.Json.JsonConvert.SerializeObject(entity);
                            requestMsg.Content = new StringContent(str, System.Text.Encoding.UTF8, CommonConstants.MediaTypeForDeviceManagementApis);
                    }
                }

                if (customContentType != null)
                {
                    requestMsg.Content.Headers.ContentType = customContentType;
                }

                if (customContentEncoding != null && customContentEncoding.Count > 0)
                {
                    foreach (string contentEncoding in customContentEncoding)
                    {
                        requestMsg.Content.Headers.ContentEncoding.Add(contentEncoding);
                    }
                }

                return Task.FromResult(0);
            };

            if (operationTimeout != _defaultOperationTimeout && operationTimeout > TimeSpan.Zero)
            {
                return ExecuteWithOperationTimeoutAsync(
                    HttpMethod.Post,
                    new Uri(_baseAddress, requestUri),
                    operationTimeout,
                    modifyRequestMessageFunc,
                    processResponseMessageAsync,
                    errorMappingOverrides,
                    cancellationToken);
            }
            else
            {
                return ExecuteAsync(
                    HttpMethod.Post,
                    new Uri(_baseAddress, requestUri),
                    modifyRequestMessageFunc,
                    processResponseMessageAsync,
                    errorMappingOverrides,
                    cancellationToken);
            }
        }

        public async Task DeleteAsync(
            Uri requestUri,
            string ifMatch,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            IDictionary<string, string> customHeaders,
            CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                    HttpMethod.Delete,
                    new Uri(_baseAddress, requestUri),
                    (requestMsg, token) =>
                    {
                        InsertIfMatch(requestMsg, ifMatch);
                        AddCustomHeaders(requestMsg, customHeaders);
                        return Task.FromResult(default(VoidTaskResult));
                    },
                    null,
                    errorMappingOverrides,
                    cancellationToken);
        }

        private struct VoidTaskResult
        {
        }

        private Task ExecuteAsync(
            HttpMethod httpMethod,
            Uri requestUri,
            Func<HttpRequestMessage, CancellationToken, Task> modifyRequestMessageAsync,
            Func<HttpResponseMessage, CancellationToken, Task> processResponseMessageAsync,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(
                _httpClientObj,
                httpMethod,
                requestUri,
                modifyRequestMessageAsync,
                IsMappedToException,
                processResponseMessageAsync,
                errorMappingOverrides,
                cancellationToken);
        }

        private Task ExecuteWithOperationTimeoutAsync(
            HttpMethod httpMethod,
            Uri requestUri,
            TimeSpan operationTimeout,
            Func<HttpRequestMessage, CancellationToken, Task> modifyRequestMessageAsync,
            Func<HttpResponseMessage, CancellationToken, Task> processResponseMessageAsync,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            CancellationToken cancellationToken)
        {
            var cts = new CancellationTokenSource(operationTimeout);
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            return ExecuteAsync(
                _httpClientObjWithPerRequestTimeout,
                httpMethod,
                requestUri,
                modifyRequestMessageAsync,
                IsMappedToException,
                processResponseMessageAsync,
                errorMappingOverrides,
                linkedCts.Token);
        }

        private static bool IsMappedToException(HttpResponseMessage message)
        {
            bool isMappedToException = !message.IsSuccessStatusCode;

            // Get any IotHubErrorCode information from the header for special case exemption of exception throwing
            string iotHubErrorCodeAsString = message.Headers.GetFirstValueOrNull(CommonConstants.IotHubErrorCode);
            ErrorCode iotHubErrorCode;
            if (Enum.TryParse(iotHubErrorCodeAsString, out iotHubErrorCode))
            {
                switch (iotHubErrorCode)
                {
                    case ErrorCode.BulkRegistryOperationFailure:
                        isMappedToException = false;
                        break;
                }
            }

            return isMappedToException;
        }

        private async Task ExecuteAsync(
            HttpClient httpClient,
            HttpMethod httpMethod,
            Uri requestUri,
            Func<HttpRequestMessage, CancellationToken, Task> modifyRequestMessageAsync,
            Func<HttpResponseMessage, bool> isMappedToException,
            Func<HttpResponseMessage, CancellationToken, Task> processResponseMessageAsync,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMappingOverrides,
            CancellationToken cancellationToken)
        {
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> mergedErrorMapping =
                MergeErrorMapping(errorMappingOverrides);

            using (var msg = new HttpRequestMessage(httpMethod, requestUri))
            {
                msg.Headers.Add(HttpRequestHeader.Authorization.ToString(), _authenticationHeaderProvider.GetAuthorizationHeader());
                msg.Headers.Add(HttpRequestHeader.UserAgent.ToString(), Utils.GetClientVersion());

                if (modifyRequestMessageAsync != null) await modifyRequestMessageAsync(msg, cancellationToken);

                // TODO: pradeepc - find out the list of exceptions that HttpClient can throw.
                HttpResponseMessage responseMsg;
                try
                {
                    responseMsg = await httpClient.SendAsync(msg, cancellationToken);
                    if (responseMsg == null)
                    {
                        throw new InvalidOperationException("The response message was null when executing operation {0}.".FormatInvariant(httpMethod));
                    }

                    if (!isMappedToException(responseMsg))
                    {
                        if (processResponseMessageAsync != null)
                        {
                            await processResponseMessageAsync(responseMsg, cancellationToken);
                        }
                    }
                }
                catch (AggregateException ex)
                {
                    var innerExceptions = ex.Flatten().InnerExceptions;
                    //if (innerExceptions.Any(Fx.IsFatal))
                    //{
                    //    throw;
                    //}

                    // Apparently HttpClient throws AggregateException when a timeout occurs.
                    // TODO: pradeepc - need to confirm this with ASP.NET team
                    if (innerExceptions.Any(e => e is TimeoutException))
                    {
                        throw new ProvisioningServiceClientTransportException(ex.Message, ex);
                    }

                    throw new ProvisioningServiceClientException(ex.Message, ex);
                }
                catch (TimeoutException ex)
                {
                    throw new ProvisioningServiceClientTransportException(ex.Message, ex);
                }
                catch (IOException ex)
                {
                    throw new ProvisioningServiceClientTransportException(ex.Message, ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new ProvisioningServiceClientTransportException(ex.Message, ex);
                }
                catch (TaskCanceledException ex)
                {
                    // Unfortunately TaskCanceledException is thrown when HttpClient times out.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new ProvisioningServiceClientException(ex.Message, ex);
                    }

                    throw new ProvisioningServiceClientTransportException(string.Format(CultureInfo.InvariantCulture, "The {0} operation timed out.", httpMethod), ex);
                }
                catch (Exception ex)
                {
                    //if (Fx.IsFatal(ex)) throw;

                    throw new ProvisioningServiceClientException(ex.Message, ex);
                }

                if (isMappedToException(responseMsg))
                {
                    Exception mappedEx = await MapToExceptionAsync(responseMsg, mergedErrorMapping);
                    throw mappedEx;
                }
            }
        }

        private static async Task<Exception> MapToExceptionAsync(
            HttpResponseMessage response,
            IDictionary<HttpStatusCode, Func<HttpResponseMessage, Task<Exception>>> errorMapping)
        {
            Func<HttpResponseMessage, Task<Exception>> func;
            if (!errorMapping.TryGetValue(response.StatusCode, out func))
            {
                return new ProvisioningServiceClientException(
                    await ExceptionHandlingHelper.GetExceptionMessageAsync(response),
                    isTransient: true);
            }

            var mapToExceptionFunc = errorMapping[response.StatusCode];
            var exception = mapToExceptionFunc(response);
            return await exception;
        }

        /// <summary>
        /// Release all HTTP resources.
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _httpClientObj?.Dispose();
                _httpClientObjWithPerRequestTimeout?.Dispose();

                _httpClientObj = null;
                _httpClientObjWithPerRequestTimeout = null;
            }

            _isDisposed = true;
        }
    }
}