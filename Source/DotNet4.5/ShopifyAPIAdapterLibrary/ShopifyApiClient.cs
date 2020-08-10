using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;


namespace ShopifyAPIAdapterLibrary
{
    /// <summary>
    /// This class is used to make Shopify API calls 
    /// </summary>
    /// <remarks>
    /// You will first need to use the ShopifyAPIAuthorizer to obtain the required authorization.
    /// </remarks>
    /// <seealso cref="http://api.shopify.com/"/>
    public class ShopifyAPIClient
    {
        public static string ApiVersion = "";

        /// <summary>
        /// Creates an instance of this class for use with making API Calls
        /// </summary>
        /// <param name="state">the authorization state required to make the API Calls</param>
        public ShopifyAPIClient(ShopifyAuthorizationState state)
        {
            this.State = state;
        }

        /// <summary>
        /// Creates an instance of this class for use with making API Calls
        /// </summary>
        /// <param name="state">the authorization state required to make the API Calls</param>
        /// <param name="translator">the translator used to transform the data between your C# client code and the Shopify API</param>
        public ShopifyAPIClient(ShopifyAuthorizationState state, IDataTranslator translator)
        {
            this.State = state;
            this.Translator = translator;
        }

        /// <summary>
        /// Make an HTTP Request to the Shopify API
        /// </summary>
        /// <param name="method">method to be used in the request</param>
        /// <param name="path">the path that should be requested</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Call(HttpMethods method, string path)
        {
            return Call(method, path, null, 0);
        }

        /// <summary>
        /// Make an HTTP Request to the Shopify API
        /// </summary>
        /// <param name="method">method to be used in the request</param>
        /// <param name="path">the path that should be requested</param>
        /// <param name="callParams">any parameters needed or expected by the API</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Call(HttpMethods method, string path, object callParams, int retryCount)
        {
            var webConfigApiVersion = ConfigurationManager.AppSettings["ShopifyApiVersion"];

            if(string.IsNullOrEmpty(ApiVersion))
            {
                if (!string.IsNullOrEmpty(webConfigApiVersion))
                {
                    ApiVersion = webConfigApiVersion;
                }
            }


            if(!string.IsNullOrEmpty(ApiVersion) && !path.Contains("/api/") && !path.Contains("/oauth/")) {
                path = path.Replace("/admin/", "/admin/api/" + ApiVersion + "/");
            }

            //reset api version
            ApiVersion = "";

            string url = String.Format("https://{0}.myshopify.com{1}", State.ShopName, path);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.ContentType = GetRequestContentType();
            request.Headers.Add("X-Shopify-Access-Token", this.State.AccessToken);
            request.Method = method.ToString();

            if (callParams != null)
            {
                if (method == HttpMethods.GET || method == HttpMethods.DELETE)
                {
                    // if no translator assume data is a query string
                    url = String.Format("{0}?{1}", url, callParams.ToString());

                    //// put params into query string
                    //StringBuilder queryString = new StringBuilder();
                    //foreach (string key in callParams.Keys)
                    //{
                    //    queryString.AppendFormat("{0}={1}", HttpUtility.UrlEncode(key), HttpUtility.UrlEncode(callParams[key]));
                    //}
                }
                else if (method == HttpMethods.POST || method == HttpMethods.PUT)
                {
                    string requestBody;
                    // put params into post body
                    if (Translator == null)
                    {
                        //assume it's a string
                        requestBody = callParams.ToString();
                    }
                    else
                    {
                        requestBody = Translator.Encode(callParams);
                    }

                    //add the requst body to the request stream
                    if (!String.IsNullOrEmpty(requestBody))
                    {
                        using (var ms = new MemoryStream())
                        {
                            using (var writer = new StreamWriter(request.GetRequestStream()))
                            {
                                writer.Write(requestBody);
                                writer.Close();
                            }
                        }
                    }
                }
            }

            try
            {

            

                var response = (HttpWebResponse)request.GetResponse();
                string result = null;
                
                string limit = response.GetResponseHeader("HTTP_X_SHOPIFY_SHOP_API_CALL_LIMIT");
                HttpContext.Current.Items["HTTP_X_SHOPIFY_SHOP_API_CALL_LIMIT"] = limit;

                double percent = Convert.ToDouble(limit.Split('/')[0]) / Convert.ToDouble(limit.Split('/')[1]);

                //cursor paging
                string linkHeader = response.GetResponseHeader("LINK");
                
                
                HttpContext.Current.Items["PAGING_LINK"] = "";
                HttpContext.Current.Items["PAGING_LINK_PREV"] = "";

                if (!string.IsNullOrEmpty(linkHeader))
                {
                    var relNext = linkHeader.Split(',').ToList().Where(x => x.Contains("rel=\"next\"")).FirstOrDefault();

                    if(!string.IsNullOrEmpty(relNext))
                    {
                        var tempUrl = relNext.Trim().Split('>')[0].Replace("<", "");
                        
                        string pageInfo = HttpUtility.ParseQueryString(new Uri(tempUrl).Query)["page_info"];

                        if(!string.IsNullOrEmpty(pageInfo))
                        {
                            HttpContext.Current.Items["PAGING_LINK"] = pageInfo;
                        }
                        
                    }

                    var relPrev = linkHeader.Split(',').ToList().Where(x => x.Contains("rel=\"previous\"")).FirstOrDefault();

                    if (!string.IsNullOrEmpty(relPrev))
                    {
                        var tempUrl = relPrev.Trim().Split('>')[0].Replace("<", "");

                        string pageInfo = HttpUtility.ParseQueryString(new Uri(tempUrl).Query)["page_info"];

                        if (!string.IsNullOrEmpty(pageInfo))
                        {
                            HttpContext.Current.Items["PAGING_LINK_PREV"] = pageInfo;
                        }

                    }


                }
                //end cursor paging


                //WriteToLog(HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"] + ", " + ((int)response.StatusCode).ToString() + ", " + method + ", " + path + " Percent: " + percent + ", HTTP_X_SHOPIFY_SHOP_API_CALL_LIMIT:" + response.GetResponseHeader("HTTP_X_SHOPIFY_SHOP_API_CALL_LIMIT"));

                using (Stream stream = response.GetResponseStream())
                {
                    StreamReader sr = new StreamReader(stream);
                    result = sr.ReadToEnd();
                    sr.Close();
                }
                                
                //At least one endpoint will return an empty string, that we need to account for.
                if (string.IsNullOrWhiteSpace(result))
                    return null;

                if (Translator != null)
                    return Translator.Decode(result);

                return result;
            }
            catch (WebException ex)
            {
                var response = ((HttpWebResponse)ex.Response);

                if((int)response.StatusCode == 429)
                {
                    double retryAfter = Convert.ToDouble(response.GetResponseHeader("Retry-After"));

                    Thread.Sleep(Convert.ToInt32(retryAfter * 1000));

                    return Call(method, path, callParams, retryCount + 1);
                }

                
                
                WriteToLog(((int)response.StatusCode).ToString() + ", " + method + ", " + path + " HTTP_X_SHOPIFY_SHOP_API_CALL_LIMIT:" + response.GetResponseHeader("HTTP_X_SHOPIFY_SHOP_API_CALL_LIMIT") + ", Shop: " + State.ShopName);

                string result = "";

                using (Stream stream = response.GetResponseStream())
                {
                    StreamReader sr = new StreamReader(stream);
                    result = sr.ReadToEnd();
                    sr.Close();
                }

                //apparently shopify can respond with 500 after timing out
                //dont retry more than 5 times. don't want to enter recursive loop in case the 500 error is unrelated to timeout
                //https://community.shopify.com/c/Shopify-APIs-SDKs/500-quot-errors-quot-quot-Internal-Server-Error-quot-from-simple/td-p/441380
                if ((int)response.StatusCode == 500 && retryCount <= 5)
                {
                    
                    //wait 3 seconds
                    Thread.Sleep(Convert.ToInt32(5000));

                    return Call(method, path, callParams, retryCount + 1);
                }

                WriteToLog(result);

                throw ex;
            }

            //throw new Exception(response)

        }

        /// <summary>
        /// Make a Get method HTTP request to the Shopify API
        /// </summary>
        /// <param name="path">the path where the API call will be made.</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Get(string path)
        {
            return Get(path, null);
        }

        /// <summary>
        /// Make a Get method HTTP request to the Shopify API
        /// </summary>
        /// <param name="path">the path where the API call will be made.</param>
        /// <param name="callParams">the querystring params</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Get(string path, NameValueCollection callParams)
        {
            return Call(HttpMethods.GET, path, callParams, 0);
        }

        /// <summary>
        /// Make a Post method HTTP request to the Shopify API
        /// </summary>
        /// <param name="path">the path where the API call will be made.</param>
        /// <param name="data">the data that this path will be expecting</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Post(string path, object data)
        {
            return Call(HttpMethods.POST, path, data, 0);
        }

        /// <summary>
        /// Make a Put method HTTP request to the Shopify API
        /// </summary>
        /// <param name="path">the path where the API call will be made.</param>
        /// <param name="data">the data that this path will be expecting</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Put(string path, object data)
        {
            return Call(HttpMethods.PUT, path, data, 0);
        }

        /// <summary>
        /// Make a Delete method HTTP request to the Shopify API
        /// </summary>
        /// <param name="path">the path where the API call will be made.</param>
        /// <seealso cref="http://api.shopify.com/"/>
        /// <returns>the server response</returns>
        public object Delete(string path)
        {
            return Call(HttpMethods.DELETE, path);
        }

        /// <summary>
        /// Get the content type that should be used for HTTP Requests
        /// </summary>
        private string GetRequestContentType()
        {
            if (Translator == null)
                return DefaultContentType;
            return Translator.GetContentType();
        }

        /// <summary>
        /// The enumeration of HTTP Methods used by the API
        /// </summary>
        public enum HttpMethods
        {
            GET,
            POST,
            PUT,
            DELETE
        }

        /// <summary>
        /// The default content type used on the HTTP Requests to the Shopify API
        /// </summary>
        protected static readonly string DefaultContentType = "application/json";

        /// <summary>
        /// The state required to make API calls.  It contains the access token and
        /// the name of the shop that your app will make calls on behalf of
        /// </summary>
        protected ShopifyAuthorizationState State { get; set; }

        /// <summary>
        /// Used to translate the data sent and recieved by the Shopify API
        /// </summary>
        /// <example>
        /// This could be used to translate from C# objects to XML or JSON.  Thus making your code
        /// that consumes this class much more clean
        /// </example>
        protected IDataTranslator Translator { get; set; }

        private void WriteToLog(string info)
        {
            string file = HttpContext.Current.Server.MapPath("~/apilog.txt");

            System.IO.File.AppendAllText(file, info + Environment.NewLine);
        }
    }


}
