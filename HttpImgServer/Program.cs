using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HttpImgServer
{
    public class ImgServer
    {
        private const string UploadDirectory = "uploads";
        public static void Main(string[] args)
        {
            // 确保存储图片的目录存在
            if (!Directory.Exists(UploadDirectory))
            {
                Directory.CreateDirectory(UploadDirectory);
            }

            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 8080)); // 监听8080端口
            serverSocket.Listen(100); // 最大等待连接数

            Console.WriteLine("INFO:Server started on http://localhost:8080...");

            while (true)
            {
                Console.WriteLine("————————————————");
                Console.WriteLine("INFO:wait for a client to connect...");
                var clientSocket = serverSocket.Accept();
                Console.WriteLine("INFO:Client connected.");
                //Console.WriteLine("INFO:Start handling client request...");
                HandleClient(clientSocket);
                Console.WriteLine("INFO:Client request handled.");
            }
        }

        private static void HandleClient(Socket clientSocket)
        {
            clientSocket.ReceiveTimeout = 3500;  // 设置接收超时时间
            // 设置缓冲区大小
            const int bufferSize = 8192; // 缓冲区大小根据实际情况调整
            byte[] buffer = new byte[bufferSize];
            List<byte> receivedData = new List<byte>();

            int bytesRead;
            int bytesReaded = 0;

            bytesRead = clientSocket.Receive(buffer);
            Console.WriteLine("INFO:Received Length :" + bytesRead + " bytes.");
            receivedData.AddRange(buffer.Take(bytesRead));
            bytesReaded += bytesRead;
            if (bytesRead < 4096)
            {
                //Console.WriteLine("INFO:Recived Over");
            }
            else
            {
                while ((bytesRead = clientSocket.Receive(buffer)) > 0)
                {
                    // 将接收到的字节添加到接收数据列表
                    Console.WriteLine("INFO:Received Length :" + bytesRead + " bytes.");
                    receivedData.AddRange(buffer.Take(bytesRead));
                    bytesReaded += bytesRead;
                    // 如果接收的数据少于缓冲区的大小，表示接收完毕
                    if (bytesRead < bufferSize)
                    {
                        break;
                    }
                }

                //Console.WriteLine("INFO:Recived Over");
            }

            // 处理接收到的完整数据
            byte[] fileData = receivedData.ToArray();

            string httpMessageString = Encoding.UTF8.GetString(fileData, 0, bytesReaded); // 解析数据为字符串
            Console.WriteLine("Received message:——————————");
            ParseHttpMessage(httpMessageString);
            Console.WriteLine("————————————End of message.");

            if (httpMessageString.StartsWith("POST"))
            {
                Console.WriteLine("INFO:Receive a POST request");
                //HandleFileBoundaryUpload(clientSocket, buffer, bytesRead);
                HandleJpgUpload(clientSocket, fileData, bytesReaded,httpMessageString);
            }
            else if (httpMessageString.StartsWith("GET"))
            {
                Console.WriteLine("INFO:Get a GET request");
                ServeFile(clientSocket, httpMessageString);
            }
            else
            {
                Console.WriteLine("INFO:Invalid request.");
                SendResponse(clientSocket, "HTTP/1.1 400 Bad Request", "Invalid Request.");
            }

            clientSocket.Close();
            //Console.WriteLine("INFO:Closing connection.");
        }

        private static void HandleJpgUpload(Socket clientSocket, byte[] buffer, int bytesRead,string httpMessage)
        {
            // 1. 确认请求头和消息体之间的分隔符 "\r\n\r\n"
            byte[] headerDelimiter = new byte[] { 13, 10, 13, 10 }; // "\r\n\r\n" 的字节表示
            int headerEndIndex = FindHeaderEndIndex(buffer, headerDelimiter, bytesRead);

            if (headerEndIndex == -1 || headerEndIndex + 4 >= bytesRead)
            {
                SendResponse(clientSocket, "HTTP/1.1 400 Bad Request", "Invalid file upload request.");
                return;
            }

            // 2. 跳过请求头和空行，获取文件数据起始位置
            int fileDataStartIndex = headerEndIndex + 4; // 跳过 \r\n\r\n 空行
            int fileDataLength = bytesRead - fileDataStartIndex;

            // 3. 保存文件
            string fileName = GetFilenameFromContentDisposition(httpMessage);
            if (fileName == null)
            {
                fileName = "unnamed.jpg";
            }
            string filePath = Path.Combine(UploadDirectory, fileName);
            byte[] fileData = new byte[fileDataLength];

            // 4. 将文件数据复制到新的数组中
            Array.Copy(buffer, fileDataStartIndex, fileData, 0, fileDataLength);

            // 5. 保存文件到磁盘
            try
            {
                File.WriteAllBytes(filePath, fileData);
                string fileUrl = "http://localhost:8080/" + UploadDirectory + "/" + fileName;
                SendResponse(clientSocket, "HTTP/1.1 200 OK", $"File uploaded successfully! Access it at {fileUrl}");
            }
            catch (Exception ex)
            {
                SendResponse(clientSocket, "HTTP/1.1 500 Internal Server Error", "Failed to save file: " + ex.Message);
            }
        }

        /// <summary>
        /// 寻找请求头的结束位置（即"\r\n\r\n"的位置）
        /// </summary>
        private static int FindHeaderEndIndex(byte[] buffer, byte[] delimiter, int bytesRead)
        {
            for (int i = 0; i < bytesRead - delimiter.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < delimiter.Length; j++)
                {
                    if (buffer[i + j] != delimiter[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1; // 未找到分隔符
        }


        private static void ServeFile(Socket clientSocket, string request)
        {
            string[] requestParts = request.Split(' ');
            string filePath = requestParts[1].Substring(1); // 移除前导 "/"

            string fullPath = filePath; //多此一举

            if (File.Exists(fullPath))
            {
                byte[] fileContent = File.ReadAllBytes(fullPath);
                clientSocket.Send(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\n\r\n"));
                clientSocket.Send(fileContent);
            }
            else
            {
                SendResponse(clientSocket, "HTTP/1.1 404 Not Found", "File not found. QAQ");
            }
        }

        private static void SendResponse(Socket clientSocket, string statusCode, string content)
        {
            string response =
                $"{statusCode}\r\n" +
                $"Content-Length: {content.Length}\r\n" +
                $"\r\n" +
                $"{content}";
            clientSocket.Send(Encoding.UTF8.GetBytes(response));
        }

        public static string GetFilenameFromContentDisposition(string httpRequest)
        {
            string[] lines = httpRequest.Split("\r\n");
            foreach (string line in lines)
            {
                if (line.StartsWith("Content-Disposition"))
                {
                    string[] parts = line.Split(';');
                    foreach (string part in parts)
                    {
                        if (part.Trim().StartsWith("filename="))
                        {
                            string filename = part.Split('=')[1].Trim().Trim('"');
                            return filename;
                        }
                    }
                }
            }
            return null;
        }

        public static void ParseHttpMessage(string httpMessage)
        {
            string[] lines = httpMessage.Split("\r\n");
            string firstLine = lines[0];
            Dictionary<string, string> headers = new Dictionary<string, string>();

            // 解析请求行或状态行
            if (firstLine.StartsWith("HTTP")) // 响应报文
            {
                string[] parts = firstLine.Split(' ');
                Console.WriteLine($"Response: {parts[0]} {parts[1]} {parts[2]}");
            }
            else // 请求报文
            {
                string[] parts = firstLine.Split(' ');
                Console.WriteLine($"Request: {parts[0]} {parts[1]} {parts[2]}");
            }

            // 解析头部
            int i = 1;
            while (!string.IsNullOrWhiteSpace(lines[i]))
            {
                string[] headerParts = lines[i].Split(": ", 2);
                headers[headerParts[0]] = headerParts[1];
                i++;
            }

            // 输出头部字段
            foreach (var header in headers)
            {
                Console.WriteLine($"{header.Key}: {header.Value}");
            }

            // 解析消息体
            int emptyLineIndex = httpMessage.IndexOf("\r\n\r\n");
            if (emptyLineIndex != -1 && emptyLineIndex + 4 < httpMessage.Length)
            {
                string body = httpMessage.Substring(emptyLineIndex + 4);
                Console.WriteLine("Body:");
                Console.WriteLine(body);
            }
        }
    }
}