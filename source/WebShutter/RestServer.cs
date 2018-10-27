/*
 * MIT License
 *
 * Copyright (c) by 2018 Simon Baer
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;

using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WebShutter
{
    /// <summary>
    /// A simple REST web server.
    /// </summary>
    internal class RestServer
    {
        private StreamSocketListener listener;
        private StorageFolder wwwroot;
        private const uint BufferSize = 8192;        

        public event EventHandler<RestCommandArgs> RestCommand;

        /// <summary>
        /// Initialize the REST server and open a socket for listening on port 80.
        /// </summary>
        /// <param name="wwwroot">storage folder from which files are served</param>
        public async void Initialise(StorageFolder wwwroot)
        {
            listener = new StreamSocketListener();
            this.wwwroot = wwwroot;

            // listen on port 80, this is the standard HTTP port (use a different port if you have a service already running on 80)
            await listener.BindServiceNameAsync("80", SocketProtectionLevel.PlainSocket);

            listener.ConnectionReceived += (sender, args) =>
            {
                // call the handle request function when a request comes in
                HandleRequest(sender, args);
            };
        }

        /// <summary>
        /// Handle a HTTP request. Only GET requests are supported.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void HandleRequest(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                StringBuilder request = new StringBuilder();

                // Handle an incoming request
                // First read the request
                using (IInputStream input = args.Socket.InputStream)
                {
                    byte[] data = new byte[BufferSize];
                    IBuffer buffer = data.AsBuffer();
                    uint dataRead = BufferSize;
                    while (dataRead == BufferSize)
                    {
                        await input.ReadAsync(buffer, BufferSize, InputStreamOptions.Partial);
                        request.Append(Encoding.UTF8.GetString(data, 0, data.Length));
                        dataRead = buffer.Length;
                    }
                }

                var requestLines = request.ToString().Split('\r', '\n');
                if ((requestLines.Length > 0) && requestLines[0].ToUpper().StartsWith("GET "))
                {
                    var url = requestLines[0].Substring(4).Split(' ')[0];

                    if (!await SendFile(args.Socket.OutputStream, url))
                    {
                        // if no static file has been served, check for a REST call
                        var e = new RestCommandArgs(url);
                        RestCommand?.Invoke(this, e);
                        if (e.IsValid)
                        {
                            var htmlResponse = string.IsNullOrEmpty(e.HtmlResponse) ? $"<html><body>Ok. ({e.Command})</body></html>" : e.HtmlResponse;
                            SendHtmlResponse(args.Socket.OutputStream, 200, "OK", htmlResponse);
                        }
                        else
                        {
                            SendHtmlResponse(args.Socket.OutputStream, 400, "Bad Request", $"<html><body><h1>Bad Request</h1>Invalid command: {e.Command}<br/>{e.ErrorMessage}</body></html>");
                        }
                    }
                }
                else
                {
                    SendHtmlResponse(args.Socket.OutputStream, 400, "Bad Request", "<html><body>Bad Request</body></html>");
                }
            }
            catch (Exception ex)
            {
                SendHtmlResponse(args.Socket.OutputStream, 500, "Internal Server Error", $"<html><body>Internal Server Error{ex}</body></html>");
            }
        }

        /// <summary>
        /// Send a HTTP response with a static file.
        /// </summary>
        /// <param name="outputStream">output stream</param>
        /// <param name="url">URL of static file in wwwroot folder</param>
        /// <returns>true if file has been serverd</returns>
        private async Task<bool> SendFile(IOutputStream outputStream, string url)
        {
            try
            {
                // remove any URL parameters after ?
                var urlParts = url.Split('?')[0].Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (urlParts.Length == 0)
                {
                    urlParts = new[] { "index.html" };
                }
                if (urlParts.Length == 1)
                {
                    if (urlParts[0].ToLower() == "api")
                    {
                        urlParts[0] = "api.html";
                    }
                    var file = await wwwroot.GetFileAsync(urlParts[0]);
                    var buffer = await FileIO.ReadBufferAsync(file);
                    var ext = Path.GetExtension(urlParts[0]);
                    var mimeType = GetMimeTypeFromExtension(ext);
                    var useCache = GetCache(ext);
                    SendResponse(outputStream, 200, "OK", buffer.ToArray(), mimeType, useCache);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Send a HTTP response with HTML body.
        /// </summary>
        /// <param name="outputStream">output stream</param>
        /// <param name="httpStatusCode">HTTP status code</param>
        /// <param name="httpStatusText">HTTP status text</param>
        /// <param name="htmlBody">HTML body</param>
        private void SendHtmlResponse(IOutputStream outputStream, int httpStatusCode, string httpStatusText, string html)
        {
            SendResponse(outputStream, httpStatusCode, httpStatusText, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", false);
        }

        /// <summary>
        /// Send a HTTP response.
        /// </summary>
        /// <param name="outputStream">output stream</param>
        /// <param name="httpStatusCode">HTTP status code</param>
        /// <param name="httpStatusText">HTTP status text</param>
        /// <param name="content">response content body</param>
        /// <param name="mimeType">mime type</param>
        /// <param name="cache">whether the response should be cached</param>
        private async void SendResponse(IOutputStream outputStream, int httpStatusCode, string httpStatusText, byte[] content, string mimeType, bool cache)
        {
            // Send a response back
            using (IOutputStream output = outputStream)
            {
                using (Stream response = output.AsStreamForWrite())
                {
                    var bodyStream = new MemoryStream(content);

                    var cacheControl = cache ? "Cache-Control: public, max-age=315360000\r\n" : "Cache-Control: no-cache, no-store, must-revalidate\r\n";

                    // This is a standard HTTP header so the client browser knows the bytes returned are a valid http response
                    var header = $"HTTP/1.1 {httpStatusCode} {httpStatusText}\r\n" +
                        "Server:Raspberry Pi 3\r\n" +
                        $"Content-Length: {bodyStream.Length}\r\n" +
                        $"Content-Type: {mimeType}\r\n" +
                        cacheControl +
                        "Connection: close\r\n\r\n";

                    byte[] headerArray = Encoding.UTF8.GetBytes(header);

                    // send the header with the body inclded to the client
                    await response.WriteAsync(headerArray, 0, headerArray.Length);
                    await bodyStream.CopyToAsync(response);
                    await response.FlushAsync();
                }
            }
        }

        /// <summary>
        /// Returns the mime type for the given file extension.
        /// </summary>
        /// <param name="ext">file extension</param>
        /// <returns>mime type</returns>
        private string GetMimeTypeFromExtension(string ext)
        {
            switch (ext.ToLower())
            {
                case ".txt":
                    return "text/plain";
                case ".html":
                case ".htm":
                    return "text/html; charset=utf-8";
                case ".css":
                    return "text/css";
                case ".js":
                    return "application/javascript; charset=utf-8";
                case ".gif":
                    return "image/gif";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                default:
                    return "application/octet-stream";
            }
        }

        /// <summary>
        /// Returns a flag whether a file with the given extension should be cached or not.
        /// </summary>
        /// <param name="ext">file extension</param>
        /// <returns>true if file should be cached</returns>
        private bool GetCache(string ext)
        {
            switch (ext.ToLower())
            {
                case ".js":
                case ".css":
                    return true;
                default:
                    return false;
            }
        }
    }
}
