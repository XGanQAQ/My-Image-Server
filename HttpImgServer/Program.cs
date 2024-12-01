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
        private static string _boundary = "--_boundary";
        public static void Main(string[] args)
        {
            // 确保存储图片的目录存在
            if (!Directory.Exists(UploadDirectory))
            {
                Directory.CreateDirectory(UploadDirectory);
            }

            var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 8080)); // 监听8080端口
            serverSocket.Listen(10); // 最大等待连接数

            Console.WriteLine("Server started on http://localhost:8080...");

            while (true)
            {
                var clientSocket = serverSocket.Accept();
                HandleClient(clientSocket);
            }
        }

        private static void HandleClient(Socket clientSocket)
        {
            byte[] buffer = new byte[1024 * 8]; // 接收缓冲区
            int bytesRead = clientSocket.Receive(buffer); // 读取到的数据长度
            string httpMessageString = Encoding.UTF8.GetString(buffer, 0, bytesRead); // 解析数据为字符串
            Console.WriteLine("Received message:");
            ParseHttpMessage(httpMessageString);
            Console.WriteLine("End of message.");
            
            if (httpMessageString.StartsWith("POST"))
            {
                Console.WriteLine("Receive a POST request");
                //HandleFileBoundaryUpload(clientSocket, buffer, bytesRead);
                HandleJpgUpload(clientSocket,buffer,bytesRead);
            }
            else if (httpMessageString.StartsWith("GET"))
            {
                Console.WriteLine("Get a GET request");
                ServeFile(clientSocket, httpMessageString);
            }
            else
            {
                Console.WriteLine("Invalid request.");
                SendResponse(clientSocket, "HTTP/1.1 400 Bad Request", "Invalid Request.");
            }

            clientSocket.Close();
        }

        private static void HandleFileBoundaryUpload(Socket clientSocket, byte[] buffer, int bytesRead)
        {
            string str = _boundary ;
            byte[] byteArray = Encoding.UTF8.GetBytes(str);
            
            int startIndex = Array.IndexOf(buffer, byteArray);
            int endIndex = Array.LastIndexOf(buffer,  byteArray);

            if (startIndex == -1 || endIndex == -1)
            {
                SendResponse(clientSocket, "HTTP/1.1 400 Bad Request", "Invalid file upload request.");
                return;
            }

            string fileName = "uploaded_image.jpg";
            string filePath = Path.Combine(UploadDirectory, fileName);
            byte[] fileData = new byte[bytesRead - (endIndex + _boundary.Length)];

            Array.Copy(buffer, endIndex + _boundary.Length, fileData, 0, fileData.Length);

            // 保存文件
            File.WriteAllBytes(filePath, fileData);
            
            string fileUrl = "http://localhost:8080/" + UploadDirectory + "/" + fileName;
            
            SendResponse(clientSocket, "HTTP/1.1 200 OK", $"File uploaded successfully! Access it at {fileUrl}");
        }

        private static void HandleJpgUpload(Socket clientSocket, byte[] buffer, int bytesRead)
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
            int fileDataStartIndex = headerEndIndex + 4;  // 跳过 \r\n\r\n 空行
            int fileDataLength = bytesRead - fileDataStartIndex;

            // 3. 保存文件
            string fileName = "uploaded_image.jpg";
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

