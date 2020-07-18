using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ReeCode
{
    public class ReeWebClient
    {
        public string DownloadString(string url)
        {
            var response = GetResponse(url);
            return response.Body;
        }

        public (HttpHeader Header, string Body) GetResponse(string url)
        {
            Uri theURL = new Uri(url);
            Console.OutputEncoding = Encoding.UTF8;
            HttpHeader receiverHeader;
            Byte[] buffer = new Byte[1];
            using (Socket bannerGrabSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                bannerGrabSocket.ReceiveTimeout = 5000;
                bannerGrabSocket.SendTimeout = 5000;
                try
                {
                    var result = bannerGrabSocket.BeginConnect(theURL.Host, 80, null, null); // Error if an invalid IP
                    bool success = result.AsyncWaitHandle.WaitOne(5000, true);
                    if (success)
                    {
                        // It got a reply
                        if (bannerGrabSocket.Connected)
                        {
                            // And that reply is that it's open!
                            string header = "GET " + theURL.AbsolutePath + " HTTP/1.1" + Environment.NewLine;
                            header += "Host: " + theURL.Host + Environment.NewLine;
                            header += "User-Agent: curl/7.55.1" + Environment.NewLine;
                            header += "Accept: */*" + Environment.NewLine;
                            header += Environment.NewLine;
                            Byte[] cmdBytes = Encoding.ASCII.GetBytes(header.ToCharArray());
                            bannerGrabSocket.Send(cmdBytes, cmdBytes.Length, 0);
                            string headerString = "";
                            string bodyString = "";
                            byte[] bodyBuff = new byte[0];
                            while (true)
                            {
                                bannerGrabSocket.Receive(buffer, 0, 1, 0);
                                headerString += Encoding.ASCII.GetString(buffer);
                                if (headerString.Contains("\r\n\r\n"))
                                {
                                    // header is received, parsing content length
                                    receiverHeader = new HttpHeader(headerString);
                                    if (headerString.Contains("Content-Length:"))
                                    {
                                        Regex reg = new Regex("\\\r\nContent-Length: (.*?)\\\r\n"); // :|
                                        Match m = reg.Match(headerString);
                                        int contentLength = int.Parse(m.Groups[1].ToString());

                                        // read the body
                                        bodyBuff = new byte[contentLength];
                                        bannerGrabSocket.Receive(bodyBuff, 0, contentLength, 0);
                                        bodyString = Encoding.ASCII.GetString(bodyBuff);
                                    }
                                    else if (headerString.Contains("Transfer-Encoding: chunked"))
                                    {
                                        int chunkSize = -1;
                                        string chunkedIntData = "";
                                        string chunkedString = "";
                                        while (chunkSize != 0)
                                        {
                                            chunkedIntData = "";
                                            while (true)
                                            {
                                                bannerGrabSocket.Receive(buffer, 0, 1, 0);
                                                chunkedIntData += Encoding.ASCII.GetString(buffer);
                                                if (chunkedIntData.EndsWith("\r\n"))
                                                {
                                                    chunkedIntData = chunkedIntData.Trim("\r\n".ToCharArray());
                                                    try
                                                    {
                                                        chunkSize = int.Parse(chunkedIntData, System.Globalization.NumberStyles.HexNumber);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine("Error - ReeWebClient.GetResponse.ChunkSizeException: Unable to parse chunk size: " + chunkedIntData);
                                                        return (null, "Error - ReeWebClient.GetResponse.ChunkSizeException: Unable to parse chunk size: " + chunkedIntData);
                                                    }
                                                    // Console.WriteLine(chunkSize);
                                                    bodyBuff = new byte[chunkSize];
                                                    for (int j = 0; j < chunkSize; j++)
                                                    {
                                                        bannerGrabSocket.Receive(bodyBuff, j, 1, 0);
                                                    }
                                                    bodyString += Encoding.UTF8.GetString(bodyBuff);
                                                    bannerGrabSocket.Receive(new byte[2], 0, 2, 0);
                                                    break;
                                                }
                                            }
                                        }
                                        // Console.WriteLine("Found a Chunk Size of 0! Ending...");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Error - ReeWebClient.GetResponse.MalformedHeaderException: " + headerString);
                                        return (null, "Error - ReeWebClient.GetResponse.MalformedHeaderException: " + headerString);
                                    }
                                    break;
                                }
                            }

                            // End - Now deal!
                            bannerGrabSocket.Close();

                            // Response contains header, 2 new lines, then the body
                            return (receiverHeader, bodyString);
                        }
                        else
                        {
                            // And that reply is that it's closed
                            bannerGrabSocket.Close();
                            return (null, "Reecon - Closed");
                        }
                    }
                    else
                    {
                        // Failed - Probably timed out
                        bannerGrabSocket.Close();
                        return (null, "Reecon - Closed");
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine("Error - ReeWebClient.GetResponse.SocketException: " + ex.Message);
                    return (null, "Error - ReeWebClient.GetResponse.SocketException: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error - ReeWebClient.GetResponse.Exception: " + ex.Message);
                    return (null, "Error - ReeWebClient.GetResponse.Exception: " + ex.Message);
                }
                finally
                {
                    bannerGrabSocket.Close();
                }
            }
        }

        public class ResponseCode
        {
            public int Number = 0;
            public string Name = "";
        }

        public class HttpHeader
        {
            public ResponseCode ResponseCode = new ResponseCode(); // :|
            public DateTime Date = new DateTime();
            public string Location = "";
            public string Server = "";
            public string ContentType = "";
            public int ContentLength = 0;
            public string Connection = "";
            public Dictionary<string, string> Cookies = new Dictionary<string, string>();
            /*
                To Parse: Expires: -1
                To Parse: Cache-Control: private, max-age=0
                To Parse: P3P: CP="This is not a P3P policy! See g.co/p3phelp for more info."
                To Parse: X-XSS-Protection: 0
                To Parse: X-Frame-Options: SAMEORIGIN
                To Parse: Accept-Ranges: none
                To Parse: Vary: Accept-Encoding
                To Parse: Transfer-Encoding: chunked
            */
            public HttpHeader(string inputString)
            {
                List<string> headerItems = inputString.Split(Environment.NewLine.ToCharArray()).ToList();
                headerItems.RemoveAll(string.IsNullOrEmpty);
                foreach (string item in headerItems)
                {
                    // HTTP/1.1 301 Moved Permanently
                    if (item.StartsWith("HTTP/1.1"))
                    {
                        ResponseCode.Number = int.Parse(item.Substring(9, 3));
                        ResponseCode.Name = item.Remove(0, 13);
                        continue;
                    }
                    // Date: Fri, 03 Jul 2020 12:26:23 GMT
                    else if (item.StartsWith("Date"))
                    {
                        Date = DateTime.Parse(item.Remove(0, 6));
                        continue;
                    }
                    // Location: https://hackthebox.eu/
                    else if (item.StartsWith("Location"))
                    {
                        Location = item.Remove(0, 10);
                        continue;
                    }
                    // Server: cloudflare
                    else if (item.StartsWith("Server"))
                    {
                        Server = item.Remove(0, 8);
                        continue;
                    }
                    // Content-Type: text/html
                    else if (item.StartsWith("Content-Type"))
                    {
                        ContentType = item.Remove(0, 14);
                        continue;
                    }
                    // Content-Length: 178
                    else if (item.StartsWith("Content-Length"))
                    {
                        ContentLength = int.Parse(item.Remove(0, 16));
                        continue;
                    }
                    // Connection: keep-alive
                    else if (item.StartsWith("Connection"))
                    {
                        Connection = item.Remove(0, 12);
                        continue;
                    }
                    // To Parse: Set-Cookie: expires=Mon, 17-Aug-2020 17:10:10 GMT; path=/; domain=.google.com; Secure
                    // A Cookie value can contain an '=' character
                    else if (item.StartsWith("Set-Cookie"))
                    {
                        string cookieItem = item.Remove(0, 12);
                        List<string> cookieList = cookieItem.Split(';').ToList();
                        foreach (string cookie in cookieList)
                        {
                            string cookieName = "";
                            string cookieValue = "";
                            if (cookie.IndexOf('=') == -1)
                            {
                                cookieName = cookie;
                                Cookies[cookieName] = cookieValue;
                                continue;
                            }
                            cookieName = cookie.Split('=')[0];
                            cookieValue = cookie.Remove(0, cookieName.Length + 1);
                            Cookies[cookieName] = cookieValue;
                        }
                        continue;
                    }
                    // Console.WriteLine("ReeWebClient - Unknown Header: " + item);
                }
            }
        }
    }
}