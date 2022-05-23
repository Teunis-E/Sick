using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

//Mogelijkheid maken om data te verwijderen
//Maximale data schrijven
//Maximale data lezen

namespace IFM_DTE804_console
{
    class Program
    {
        #region Declarations
        //Declarate variables
        private static TcpClient _client;
        private static NetworkStream _clientStream;
        private static char[] _delimiterChars = { '_', '\r', '\n' };
        private static List<DataEPC> _listEPC = new List<DataEPC>();
        private static List<string> _diagData = new List<string>();

        //Create class to safe the tag data
        private class DataEPC
        {
            public string TagEPC { get; set; }
            public float TagRSSI { get; set; }
            public int TagMemory { get; set; }
            public int LengthEPC { get; set; }
            public int Index { get; set; }
        }

        //Check enummeration of the reader
        enum ReadEPCResult
        {
            NoTagFound,
            TagFound
        }

        //Current status of the program
        enum Status
        {
            None,
            RefreshOnce,
            RefreshContinues,
            ReadMemory,
            WriteMemory,
            SpecificCommand,
            CloseApplication
        }
        #endregion

        static void Main()
        {
            //Create default IP, with possibility to connect with another IP.
            var ipAdress = "192.168.10.183";
            Console.WriteLine("Enter the IP adress to connect:");

            var suggestedIP = Console.ReadLine();
            if (!string.IsNullOrEmpty(suggestedIP))
                ipAdress = suggestedIP;

            Console.WriteLine($"Program started, try to connect to the DTE804 via IP: {ipAdress} ...");

            //Connect and configure DTE804
            while (!Connect(ipAdress))
            {
                Console.WriteLine("Try again with each key, exit with 'e'.");
                if (Console.ReadKey().Key == ConsoleKey.E)
                    ExitConsole();
            }

            Console.WriteLine($"Connected");

            while (true)
                MainMenu();
        }

        #region Main menu
        private static void MainMenu()
        {
            //Get the EPC results
            var result = RefreshEPCData();

            Console.Clear();

            //Set keyboard status on default
            Status keyboard = Status.None;

            switch (result)
            {
                //By no tag found, refresh unil tag is found
                case ReadEPCResult.NoTagFound:
                    Console.WriteLine("No tags found.");
                    while (_listEPC.Count() == 0)
                    {
                        RefreshEPCData();
                        Thread.Sleep(50);
                    }
                    MainMenu();
                    break;

                //Create menu if tags are found.
                case ReadEPCResult.TagFound:
                    //Show all tag information
                    Console.WriteLine("Tags found:");
                    Console.WriteLine("Index\tLength\tMemory\tRSSI\tEPC");
                    foreach (var EPC in _listEPC)
                        Console.WriteLine($"{EPC.Index}:\t{EPC.LengthEPC}\t{EPC.TagMemory} bits\t{EPC.TagRSSI}\t{EPC.TagEPC}");
                    Console.WriteLine();

                    //Get options
                    Console.WriteLine("What do you want to do?");
                    Console.WriteLine();
                    Console.WriteLine("1:\tRefresh EPCs (once)");
                    Console.WriteLine("2:\tRefresh EPCs (continues)");
                    Console.WriteLine("3:\tRead user memory");
                    Console.WriteLine("4:\tWrite user memory");
                    Console.WriteLine("5:\tSpecific command");
                    Console.WriteLine();
                    Console.WriteLine("e:\tExit");
                    Console.WriteLine();
                    Console.Write("Your choice: ");

                    //Save options on keyboard status
                    switch (Console.ReadKey().Key)
                    {
                        case ConsoleKey.NumPad1:
                        case ConsoleKey.D1:
                            keyboard = Status.RefreshOnce;
                            break;

                        case ConsoleKey.NumPad2:
                        case ConsoleKey.D2:
                            keyboard = Status.RefreshContinues;
                            break;

                        case ConsoleKey.NumPad3:
                        case ConsoleKey.D3:
                            keyboard = Status.ReadMemory;
                            break;

                        case ConsoleKey.NumPad4:
                        case ConsoleKey.D4:
                            keyboard = Status.WriteMemory;
                            break;

                        case ConsoleKey.NumPad5:
                        case ConsoleKey.D5:
                            keyboard = Status.SpecificCommand;
                            break;

                        case ConsoleKey.E:
                            keyboard = Status.CloseApplication;
                            break;
                    }
                    break;

            }

            switch (keyboard)
            {
                //Refresh once by this option
                case Status.RefreshOnce:
                    keyboard = Status.None;
                    MainMenu();
                    break;

                //Refresh continues with no options for 20 seconds
                case Status.RefreshContinues:
                    Console.Clear();

                    Console.WriteLine("Tags found:");
                    Console.WriteLine("Index\tLength\tMemory\tRSSI\tEPC");
                    foreach (var item in _listEPC)
                        Console.WriteLine($"{item.Index}:\t{item.LengthEPC}\t{item.TagMemory} bits\t{item.TagRSSI}\t{item.TagEPC}");

                    Console.WriteLine();
                    Console.WriteLine("Press any key to continue");

                    while (!Console.KeyAvailable)
                    {
                        var oldList = _listEPC.Select(i => new { i.TagEPC, i.TagRSSI }).ToArray();
                        RefreshEPCData();
                        var newList = _listEPC.Select(i => new { i.TagEPC, i.TagRSSI }).ToArray();

                        if (!oldList.SequenceEqual(newList))
                        {
                            Console.Clear();
                            Console.WriteLine("Tags found:");
                            Console.WriteLine("Index\tLength\tMemory\tRSSI\tEPC");
                            foreach (var item in _listEPC)
                                Console.WriteLine($"{item.Index}:\t{item.LengthEPC}\t{item.TagMemory} bits\t{item.TagRSSI}\t{item.TagEPC}");

                            Console.WriteLine();
                            Console.WriteLine("Press any key to continue");
                        }

                        Thread.Sleep(100);
                    }
                    break;

                //Read user memory of a tag
                case Status.ReadMemory:
                    keyboard = Status.None;
                    //menu write EPC
                    Console.WriteLine("Read memory.");
                    String tagName = "1";
                    if (_listEPC.Count() > 1)
                    {
                        Console.WriteLine("Which EPC Index do you want to read, confirm with enter: ");
                        tagName = Console.ReadLine();
                    }
                    if (!_listEPC.Any(i => i.Index.ToString() == tagName.ToString()))
                    {
                        Console.WriteLine("EPC with that index not found. Please try again.");
                        Thread.Sleep(500);
                        MainMenu();
                    }

                    //Check of tag still excists
                    var EPC = _listEPC.First(i => i.Index.ToString() == tagName.ToString());
                    RefreshEPCData();
                    if (_listEPC.Any(i => i.TagEPC == EPC.TagEPC))
                    {
                        //send command read epc
                        var memory = ReadUserMemoryEPC(EPC);

                        Console.WriteLine();
                        Console.WriteLine($"User Memory: {memory}");
                    }
                    else
                        Console.WriteLine($"Tag {EPC.TagEPC} disconnected. Please try again.");

                    Console.WriteLine();
                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //Write user memory on a tag
                case Status.WriteMemory:
                    keyboard = Status.None;
                    //menu write EPC
                    Console.WriteLine("Write memory");
                    tagName = "1";
                    if (_listEPC.Count() > 1)
                    {
                        Console.WriteLine("Which EPC Index do you want to write, confirm with enter: ");
                        tagName = Console.ReadLine();
                    }

                    if (!_listEPC.Any(i => i.Index.ToString() == tagName.ToString()))
                    {
                        Console.WriteLine("EPC with that index not found. Please try again.");
                        Thread.Sleep(500);
                        MainMenu();
                    }

                    //Check of tag still excists
                    EPC = _listEPC.First(i => i.Index.ToString() == tagName.ToString());
                    RefreshEPCData();
                    if (_listEPC.Any(i => i.TagEPC == EPC.TagEPC))
                    {
                        Console.WriteLine("Which text do you want to write, confirm with enter: ");
                        string text = Console.ReadLine();
                        //send command write epc
                        WriteUserMemoryEPC(EPC, text);
                    }
                    else
                        Console.WriteLine($"Tag {EPC.TagEPC} disconnected. Please try again.");

                    Console.WriteLine();
                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //Get a specific option
                case Status.SpecificCommand:
                    keyboard = Status.None;
                    Console.WriteLine("Specific command");
                    Console.WriteLine("Which command do you want to write, confirm with enter: ");
                    string command = Console.ReadLine();
                    string feedback = SendMessage(command);
                    Console.WriteLine($"Feedback from command: {feedback}");
                    Console.WriteLine();
                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //Close application
                case Status.CloseApplication:
                    keyboard = Status.None;
                    Disconnect();
                    ExitConsole();
                    break;
            }

        }
        #endregion

        #region Connect and configure

        //Connect to DTE804 with timeout of 1 second.
        private static bool Connect(string IpAdress)
        {
            //Open TCP connection with controller
            Console.WriteLine("Connection started");
            _client = new TcpClient();

            //connection data DTE804
            var result = _client.BeginConnect(IpAdress, 33000, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (connected)
                Console.WriteLine("Sick RFU620 connected.");
            else
                Console.WriteLine("Connection with RFU620 failed.");

            if (connected)
                _client.EndConnect(result);

            Console.WriteLine();
            return connected;
        }
        #endregion

        #region Global control
        //Send default message
        private static string SendMessage(string msg = "")
        {

            //NetworkStream 
            _clientStream = _client.GetStream();

            ASCIIEncoding encoder = new ASCIIEncoding();

            msg = msg.Replace("<STX>", encoder.GetString(new byte[] { 0x02 }));
            msg = msg.Replace("<ETX>", encoder.GetString(new byte[] { 0x03 }));



            byte[] buffer = encoder.GetBytes(msg);

            _clientStream.Write(buffer, 0, buffer.Length);
            _clientStream.Flush();
            // Receive the TcpServer response. 

            // Buffer to store the response bytes. 
            Byte[] data = new Byte[1024];

            // String to store the response ASCII representation. 
            String responseData = String.Empty;

            Int32 bytes;
            //_clientStream.ReadTimeout = 15000;

            // Read the first batch of the TcpServer response bytes. 
            bytes = _clientStream.Read(data, 0, data.Length);

            responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes);

            //write response data to text box
            responseData = responseData.Replace(encoder.GetString(new byte[] { 0x02 }), "");
            responseData = responseData.Replace(encoder.GetString(new byte[] { 0x03 }), "");

            return responseData;
        }

        //Refresh EPC Data and save it in a global list.
        private static ReadEPCResult RefreshEPCData()
        {
            _listEPC.Clear();

            //send command via TCP/IP
            String rawAnswer = SendMessage("GetTags");

            //split answer by seperator "_"
            var tagData = rawAnswer.Split("\r\n").ToList();

            var numberOfTags = int.Parse(tagData.First());
            if (numberOfTags == 0)
                return ReadEPCResult.NoTagFound;

            tagData.RemoveAt(0);
            tagData.Remove(tagData.Last());

            foreach (var item in tagData)
            {
                String[] splittedData = item.Split("_");

                var newTag = new DataEPC();
                newTag.TagEPC = splittedData[0];
                newTag.TagRSSI = float.Parse(splittedData[1]);
                //newTag.LengthEPC = int.Parse(splittedData[2]);
                //newTag.TagMemory = int.Parse(splittedData[3]);
                _listEPC.Add(newTag);
            }

            _listEPC = _listEPC.OrderByDescending(item => item.TagRSSI).ToList();
            int index = 1;

            foreach (var EPC in _listEPC)
            {
                EPC.Index = index;
                index++;
            }

            return ReadEPCResult.TagFound;
        }

        //Disconnect TCP client;
        private static void Disconnect()
        {
            //Close connection to DTE
            _client.Close();
            _client = null;
            _clientStream = null;
        }

        private static void ExitConsole()
        {
            Console.Clear();
            Environment.Exit(0);
        }
        #endregion

        #region Options
        //Read user memory of the tag
        private static string ReadUserMemoryEPC(DataEPC dataEPC)
        {
            //send command via TCP/IP
            String rawAnswer = SendMessage($"<STX>sMN TAreadTagData 0 3 0 6 32<ETX>");

            String[] splittedData = rawAnswer.Split(" ");

            Console.WriteLine(rawAnswer);
            return HexToAscii(splittedData.Last());
        }

        //Write user memory of the tag
        private static void WriteUserMemoryEPC(DataEPC dataEPCs, string Text)
        {
            ASCIIEncoding encoder = new ASCIIEncoding();
            var hexMessage = AsciiToHex(Text);
            int wordCounter = encoder.GetBytes(hexMessage).Length / 2;
            var byteLength = hexMessage.Length /2;

            //send command via TCP/IP
            String rawAnswer = SendMessage($"<STX>sMN TAwriteTagData 0 3 0 {wordCounter} 32 +{byteLength} {hexMessage}<ETX>");
        }
        #endregion

        public static string AsciiToHex(string asciiString)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in asciiString)
            {
                builder.Append(Convert.ToInt32(c).ToString("X"));
            }
            return builder.ToString();
        }

        public static string HexToAscii(string hexString)
        {
            string res = String.Empty;

            for (int a = 0; a < hexString.Length; a = a + 2)
            {
                string Char2Convert = hexString.Substring(a, 2);
                int n = Convert.ToInt32(Char2Convert, 16);
                char c = (char)n;

                res += c.ToString();
            }

            return res;
        }
    }
}

