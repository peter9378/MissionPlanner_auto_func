using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Runtime.Serialization.Json;

namespace MissionPlanner
{


    public class NetworkProcess
    {
        // Thread signal
        public ManualResetEvent allDone = new ManualResetEvent(false);

        public NetworkProcess()
        {
            Console.WriteLine("Hello world");
        }

        ~NetworkProcess() { /*SaveAllToFile();*/ }

        Dictionary<string, List<DataType>> totalData = new Dictionary<string, List<DataType>>();

        // 1 client prr 1 MP (drone) : 미션플래너에 한 드론연결이라 싱글유저라서 일단 tick 단일 로 해둠
        private bool _tick = false;
        private bool isEnd = false;
        private bool isStarted = false;
        private string curUser;
        // return tick
        public bool tick { get { return _tick; } }
        public bool end { get { return isEnd; } }
        public bool started { get { return isStarted; } }

        public string User { get { return curUser; } }

        public void StartListening()
        {

            // Data buffer for incoming data.
            byte[] bytes = new Byte[1024];

            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            //FileStream fs = File.Create("host.txt");
            //foreach (var item in ipHostInfo.AddressList)
            //{
            //    fs.Write(Encoding.UTF8.GetBytes(item.ToString()),0, Encoding.UTF8.GetBytes(item.ToString()).Length);
            //}
            //fs.Close();
            IPAddress ipAddress = ipHostInfo.AddressList[2];    // swan home: [5], swan mac(win): [2]
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 19871);

            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);

                    // Wait until a connection is made before continuing.
                    allDone.WaitOne();
                }

            }
            catch (ThreadAbortException e)
            {
                listener.Close();
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }


            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }


    
    

        public void AcceptCallback(IAsyncResult ar)
        {
            // started init
            isStarted = false;

            // Signal the main thread to continue.
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.
            StateObject state = new StateObject();
            state.workSocket = handler;
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
        }

        public void ReadCallback(IAsyncResult ar)
        {
            

            String content = String.Empty;

            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            // Read data from the client socket. 
            int bytesRead = handler.EndReceive(ar);

            if (bytesRead > 0)
            {
                // There  might be more data, so store the data received so far.
                state.sb.Append(Encoding.ASCII.GetString(
                    state.buffer, 0, bytesRead));

                // Check for end-of-file tag. If it is not there, read 
                // more data.
                content = state.sb.ToString();
                //Console.WriteLine(content);
                
                if (content.IndexOf("<EOF>") > -1)
                {
                    // All the data has been read from the 
                    // client. Display it on the console.
                    //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                    //    content.Length, content);
                    // Echo the data back to the client.
                    //Send(handler, content);

                    //Console.WriteLine(_tick);
                    // 여기서 처리
                    isEnd= SaveData(content);
                    

                    // 0 ack으로 보내기
                    // json parse
                    // 위치 누적 저장
                    // id type lat long


                    // flush
                    state.sb.Clear();
                    
                    if (isEnd)
                    {
                        MakeReturnWP(User);
                        MainV2.instance.FlightPlanner.DAEMON_setRTLLand(this, null);
                        //state.workSocket.Close();
                        /*
                         * Set Auto mode (restart mission)
                         * Auto: 갱신
                         * 이외: 회피중 (알아서 바꿀 것)
                         */
                        if (MainV2.comPort.MAV.cs.mode.Equals("Auto"))
                            MainV2.comPort.setMode("Auto");
                        
                    }
                    else
                    {
                        // waypointfile 재로딩 및 쓰기호출
                        if (totalData[User].Count > 1)
                        {
                            MainV2.instance.FlightPlanner.DAEMON_loadwpfile(this, null);
                            MainV2.instance.FlightPlanner.DAEMON_writeLocation(this, null);
                            // Set Auto mode (restart mission)
                            if (MainV2.comPort.MAV.cs.mode.Equals("Auto"))
                                MainV2.comPort.setMode("Auto");
                        }

                        // receive again
                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            new AsyncCallback(ReadCallback), state);
                    }
                   
                }
                else
                {
                    // Not all data received. Get more.
                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
                }
            }
        }

        private void Send(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                handler.Shutdown(SocketShutdown.Both);
                handler.Close();

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        // 누적 관리
        private bool SaveData(string content)
        {
            content = content.Remove(content.Length - 5, 5);   // remove <EOF>
            Console.WriteLine(content);
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DataType));
            // string to byte
            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content))
            {
                Position = 0
            };
            //string _type;
            DataType data = (DataType)ser.ReadObject(stream);
            curUser = data.id;

            // Get Altitude
            // using flightplanner.cs srtm class 
             var altdata= srtm.getAltitude(data.latitude, data.longitude);
            double alt = 5; //altdata.alt + 4;   // add meter
            data.altitude = alt;


            // dict 확인
            List<DataType> dList;
            try
            {
                dList = totalData[curUser];

            }
            catch (KeyNotFoundException)
            {
                isStarted = true;
                totalData.Add(curUser, new List<DataType>());
                dList = totalData[curUser];
                //dList.Add(new DataType(curUser));   // home 위치 설정 필요 (아래에서 수정)
            }
            // dict 요소에 추가 (유저별)
            // 첫 add가 home
            dList.Add(data);
            
            if (dList.Count > 1)    // home 위치를 처음 수신한 곳으로 고정
                SaveLocToWPFile(curUser);   // 접속 사용자별 분리 (비동기)


            /*
            Console.WriteLine(data.id);
            Console.WriteLine(data.type);
            Console.WriteLine(data.latitude);
            Console.WriteLine(data.longitude);
            */

            // type 0 : 종료
            if (data.type.Equals("0"))
            {
                return true;
            }
            else return false;
        }

        /// <summary>
        /// 계속 갱신할 WP를 생성
        /// </summary>
        private void SaveLocToWPFile(string _id)
        {
            List<DataType> list = totalData[_id];
            // count -1 count -2 이렇게 두 요소
            DataType home = list[0];
            DataType current = list[list.Count - 1];

            
            // 다음꺼도 해야함

            // wp string make
            String wpForm = "QGC WPL 110\n";
            wpForm += ("0\t1\t0\t16\t0\t0\t0\t0\t" + home.latitude + "\t" + home.longitude + "\t"
                + home.altitude + "\t1\n");
            wpForm += ("1\t0\t3\t16\t0\t0\t0\t0\t" + current.latitude + "\t" + current.longitude + "\t"
                            + current.altitude + "\t1");

            // waypoint file format
            /*
             * QGC WPL 110
             * 0    1   0   16  0   0   0   0   <Lat>   <Long>  <Alt>   1
             * <index>  
             * QGC WPL <VERSION>
             * <INDEX> <CURRENT WP> <COORD FRAME> <COMMAND> <PARAM1> <PARAM2> <PARAM3> <PARAM4> <PARAM5/X/LONGITUDE> <PARAM6/Y/LATITUDE> <PARAM7/Z/ALTITUDE> <AUTOCONTINUE>
             * 
             * 16: waypoint
             * 21: land
             */

            // save to file
            // 파일 동시 액세스, 대기에 따른 지연을 막기 위해서 파일을 두개로 번갈아서 관리
            string padding;
            Console.WriteLine(tick);
            if (_tick)
                padding = "_1";
            else
                padding = "_0";
            FileStream fs = File.Create(_id + padding + ".waypoints");
            byte[] wpByte = Encoding.UTF8.GetBytes(wpForm);
            fs.Write(wpByte, 0, wpByte.Length);
            fs.Close();
            _tick = !_tick;
        }

        /// <summary>
        /// 소멸자에서 불림. List<DateType>를 모두 저장
        /// 같은 사용자의 경우 최근의 데이터로 갱신 (overwrite)
        /// </summary>
        private void SaveAllToFile()
        {
            // 중요도가 낮은 항목이라 일단 보류
            FileStream fs;
            string filename;
            foreach (var item in totalData)
            {
                // item (key, user)별로 파일 저장. 데이터 요소 뜯어서 wp 형식으로 맞추어야함
                filename = item.Key + "_total.waypoints";
                fs = File.Create(filename);
                foreach (var wp in item.Value)
                {

                }
            }
        }

        /// <summary>
        /// RTL Waypoint 생성
        /// </summary>
        /// <param name="_id"></param>
        private void MakeReturnWP(string _id)
        {
            List<DataType> list = totalData[_id];
            // count -1 count -2 이렇게 두 요소
            DataType home = list[0];
            
            // wp string make
            // 16 WP, 20 RTL, 21 LAND
            String wpForm = "QGC WPL 110\n";
            wpForm += ("0\t1\t0\t16\t0\t0\t0\t0\t" + home.latitude + "\t" + home.longitude + "\t"
                + home.altitude + "\t1\n"); // home
            wpForm += ("1\t0\t0\t20\t0\t0\t0\t0\t" + 0 + "\t" + 0 + "\t"
                + 0 + "\t1\n");   // RTL
            wpForm += ("2\t0\t0\t21\t0\t0\t0\t0\t" + 0 + "\t" + 0 + "\t"
                + 0 + "\t1");   // LAND

            string padding = "_return";

            FileStream fs = File.Create(_id + padding + ".waypoints");
            byte[] wpByte = Encoding.UTF8.GetBytes(wpForm);
            fs.Write(wpByte, 0, wpByte.Length);
            fs.Close();

        }

    }

    // State object for reading client data asynchronously
    public class StateObject
    {
        // Client  socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();
    }

    /// <summary>
    /// json parsing용 데이터타입
    /// </summary>
    public class DataType
    {
        public DataType() { }
        public DataType(string _id) { id = _id; }

        public string type { get; set; }  // 수신 데이터 구분하는 용도로만 쓰임
        public string id { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
        public double altitude { get; set; }
    }

   


}
