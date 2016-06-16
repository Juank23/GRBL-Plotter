﻿/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.
   
    Copyright (C) 2015-2016 Sven Hasemann contact: svenhb@web.de

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
/*  Many thanks to:
    3dpBurner Sender. A GCODE sender for GRBL based devices.
    This file is part of 3dpBurner Sender application.   
    Copyright (C) 2014-2015  Adrian V. J. (villamany) contact: villamany@gmail.com

    This project was my starting point
*/

// #define debug_streaming

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;


namespace GRBL_Plotter
{

    public partial class SerialForm : Form
    {
        public xyzPoint posWorld, posMachine, posPause, posProbe, posProbeOld;

        private string rxString;
        private string parserState = "";
        private bool serialPortOpen = false;
        public bool SerialPortOpen 
        {   get {return serialPortOpen;} }

        private Dictionary<string, double> gcodeVariable = new Dictionary<string, double>();

        public SerialForm()
        {
            InitializeComponent();
            Application.ThreadException += new System.Threading.ThreadExceptionEventHandler(Application_ThreadException);
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(Application_UnhandledException);
        }
        //Unhandled exception
        private void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            closePort();
            Exception ex = e.Exception;
            MessageBox.Show(ex.Message, "Thread exception");
        }
        private void Application_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject != null)
            {
                closePort();
                Exception ex = (Exception)e.ExceptionObject;
                MessageBox.Show(ex.Message, "Application exception");
            }
        }

        private void SerialForm_Load(object sender, EventArgs e)
        {   if ((Location.X < 0)|| (Location.Y < 0)) { Location = new Point(0,0); }
            SerialForm_Resize(sender, e);
            refreshPorts();
            updateControls();
            loadSettings();
            openPort();
        }
        private bool mainformAskClosing = false;
        private void SerialForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.lastOffsetX = posWorld.X;
            Properties.Settings.Default.lastOffsetY = posWorld.Y;
            Properties.Settings.Default.lastOffsetZ = posWorld.Z;
            saveSettings();
            if ((e.CloseReason.ToString() != "FormOwnerClosing") & !mainformAskClosing)
            {
                var window = MessageBox.Show("Serial Connection is needed.\r\nClose main window instead");
                e.Cancel = true;
                return;
            }
            else
            {
                closePort();
                mainformAskClosing = true;
            }
        }
        private void SerialForm_Resize(object sender, EventArgs e)
        {
            rtbLog.Width = this.Width - 20;
            rtbLog.Height = this.Height - 120;
            btnClear.Location = new Point(btnClear.Location.X, this.Height - 86);
            tbCommand.Location = new Point(tbCommand.Location.X, this.Height - 84);
            btnSend.Location = new Point(btnSend.Location.X, this.Height - 86);
            btnGRBLCommand0.Location = new Point(btnGRBLCommand0.Location.X, this.Height - 62);
            btnGRBLCommand1.Location = new Point(btnGRBLCommand1.Location.X, this.Height - 62);
            btnGRBLCommand2.Location = new Point(btnGRBLCommand2.Location.X, this.Height - 62);
            btnGRBLCommand3.Location = new Point(btnGRBLCommand3.Location.X, this.Height - 62);
            btnGRBLCommand4.Location = new Point(btnGRBLCommand4.Location.X, this.Height - 62);
            btnGRBLReset.Location = new Point(btnGRBLReset.Location.X, this.Height - 62);
        }

        private void timerSerialEnable(bool value)
        {
            timerSerial.Enabled = value;
        }
        private void timerSerial_Tick(object sender, EventArgs e)
        {
            if (minimizeCount > 0)
            {   minimizeCount--;
                if (minimizeCount==0)
                    this.WindowState = FormWindowState.Minimized;
            }

            if (!serialPort1.IsOpen)
            { }     // BringToFront();
            else
            {
                try
                {
                    var dataArray = new byte[] { Convert.ToByte('?') };
                    serialPort1.Write(dataArray, 0, 1);
                }
                catch (Exception er)
                {
                    logError("Retrieving GRBL status", er);
                    serialPort1.Close();
                }
            }
            if (waitForIdle)
                processSend();
        }

        private void resetVariables()
        {
            gcodeVariable.Clear();
            gcodeVariable.Add("PRBX", 0.0); // Probing coordinates
            gcodeVariable.Add("PRBY", 0.0);
            gcodeVariable.Add("PRBZ", 0.0);
            gcodeVariable.Add("PRDX", 0.0); // Probing delta coordinates
            gcodeVariable.Add("PRDY", 0.0); // delta = actual - last
            gcodeVariable.Add("PRDZ", 0.0);
            gcodeVariable.Add("MACX", 0.0); // actual Machine coordinates
            gcodeVariable.Add("MACY", 0.0);
            gcodeVariable.Add("MACZ", 0.0);
            gcodeVariable.Add("WACX", 0.0); // actual World coordinates
            gcodeVariable.Add("WACY", 0.0);
            gcodeVariable.Add("WACZ", 0.0);
            gcodeVariable.Add("MLAX", 0.0); // last Machine coordinates (before break)
            gcodeVariable.Add("MLAY", 0.0);
            gcodeVariable.Add("MLAZ", 0.0);
            gcodeVariable.Add("WLAX", 0.0); // last World coordinates (before break)
            gcodeVariable.Add("WLAY", 0.0);
            gcodeVariable.Add("WLAZ", 0.0);
        }
        private void loadSettings()
        {
            try
            {
                cbPort.Text = Properties.Settings.Default.port;
                cbBaud.Text = Properties.Settings.Default.baud;
            }
            catch (Exception e)
            {
                logError("Loading settings", e);
            }
        }
        private void saveSettings()
        {
            try
            {
                Properties.Settings.Default.port = cbPort.Text;
                Properties.Settings.Default.baud = cbBaud.Text;
                Properties.Settings.Default.Save();
            }
            catch (Exception e)
            {
                logError("Saving settings", e);
            }
        }
        private void saveLastPos()
        {
            rtbLog.AppendText(String.Format("Save last pos.: X{0} Y{1} Z{2}\r\n", posWorld.X, posWorld.Y, posWorld.Z));
            Properties.Settings.Default.lastOffsetX = posWorld.X;
            Properties.Settings.Default.lastOffsetY = posWorld.Y;
            Properties.Settings.Default.lastOffsetZ = posWorld.Z;
            Properties.Settings.Default.Save();
        }

        private void updateControls()
        {
            bool isConnected = serialPort1.IsOpen;
            serialPortOpen = isConnected;
            bool isSensing = isStreaming;
            cbPort.Enabled = !isConnected;
            cbBaud.Enabled = !isConnected;
            btnScanPort.Enabled = !isConnected;
            btnClear.Enabled = isConnected;
            tbCommand.Enabled = isConnected & !isSensing;
            btnSend.Enabled = isConnected & !isSensing;
            btnGRBLCommand0.Enabled = isConnected & !isSensing;
            btnGRBLCommand1.Enabled = isConnected & !isSensing;
            btnGRBLCommand2.Enabled = isConnected & !isSensing;
            btnGRBLCommand3.Enabled = isConnected & !isSensing;
            btnGRBLCommand4.Enabled = isConnected & !isSensing;
            btnGRBLReset.Enabled = isConnected;// & !isSensing;
        }

        private void logError(string message, Exception err)
        {
            string textmsg = "\r\n[ERROR]: " + message + ". ";
            if (err != null) textmsg += err.Message;
            textmsg += "\r\n";
            rtbLog.AppendText(textmsg);
            rtbLog.ScrollToCaret();
        }
        public void logErrorThr(object sender, EventArgs e)
        {
            logError(mens, err);
            updateControls();
        }
        public void addToLog(string text)
        {
            rtbLog.AppendText(text + "\r");
            rtbLog.ScrollToCaret();
        }

        private void btnScanPort_Click(object sender, EventArgs e)
        { refreshPorts(); }
        private void refreshPorts()
        {
            List<String> tList = new List<String>();
            cbPort.Items.Clear();
            foreach (string s in System.IO.Ports.SerialPort.GetPortNames()) tList.Add(s);
            if (tList.Count < 1) logError("No serial ports found", null);
            else
            {
                tList.Sort();
                cbPort.Items.AddRange(tList.ToArray());
            }
        }
        private void btnOpenPort_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
                closePort();
            else
                openPort();
        }
        private int minimizeCount = 0;
        private bool openPort()
        {
            try
            {
                serialPort1.PortName = cbPort.Text;
                serialPort1.BaudRate = Convert.ToInt32(cbBaud.Text);
                serialPort1.Open();
                rtbLog.Clear();
                rtbLog.AppendText("Open " + cbPort.Text + "\r\n");
                btnOpenPort.Text = "Close";
                isDataProcessing = true;
                grblReset(false);
                updateControls();
                if (Properties.Settings.Default.serialMinimize)
                    minimizeCount = 10;     // minimize window after 10 timer ticks
                timerSerial.Interval = 200;
//                this.WindowState = FormWindowState.Minimized;
                return (true);
            }
            catch (Exception err)
            {
                minimizeCount = 0;
                logError("Opening port", err);
                updateControls();
                return (false);
            }
        }
        private bool closePort()
        {
            try
            {
                if (serialPort1.IsOpen)
                {
                    serialPort1.Close();
                }
                rtbLog.AppendText("Close " + cbPort.Text + "\r\n");
                btnOpenPort.Text = "Open";
                updateControls();
                timerSerial.Interval = 1000;
                return (true);
            }
            catch (Exception err)
            {
                logError("Closing port", err);
                updateControls();
                return (false);
            }
        }

        //Send reset sentence
        public void grblReset(bool savePos = true)//Stop/reset button
        {
            if (savePos)
            {   saveLastPos(); }
            resetVariables();
            isStreaming = false;
            isStreamingPause = false;
            waitForIdle = false;
            rtbLog.AppendText("[CTRL-X]");
            var dataArray = new byte[] { 24 };//Ctrl-X
            serialPort1.Write(dataArray, 0, 1);
        }

        string mens;
        Exception err;
        private void serialPort1_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            while ((serialPort1.IsOpen) && (serialPort1.BytesToRead > 0))
            {   rxString = string.Empty;
                try
                {   rxString = serialPort1.ReadTo("\r\n");              //read line from grbl, discard CR LF
                    isDataProcessing = true;
                    this.Invoke(new EventHandler(handleRxData));        //tigger rx process 
                    while ((serialPort1.IsOpen) && (isDataProcessing)) ;  //wait previous data line processed done
                }
                catch (Exception errort)
                {   serialPort1.Close();
                    mens = "Error reading line from serial port";
                    err = errort;
                    this.Invoke(new EventHandler(logErrorThr));
                }
            }
        }
        private void handleRxData(object sender, EventArgs e)
        {
            if (rxString.IndexOf("['$' for help]") >= 0)            // reset message
            {   isDataProcessing = false;
                sendLines.Clear();
                sendLinesCount = 0;             
                sendLinesSent=0;              
                sendLinesConfirmed=0;
                grblBufferFree = grblBufferSize;
                OnRaiseStreamEvent(new StreamEventArgs(0, 0, 0, grblStreaming.reset));
                addToLog("> RESET\r\n"+rxString);
                return;
            }
            if ((rxString.Length > 0) && (rxString[0] == '<'))      // Status message with coordinates
            {   handleStatusMessage(rxString);
                isDataProcessing = false;
                return;
            }
            if (rxString.IndexOf("[PRB:") >= 0)                       // Probe message with coordinates
            {   // [PRB:-155.000,-160.000,-28.208:1]
                handleStatusMessage(rxString,true);
                addToLog(string.Format("rx> {0}", rxString));
                isDataProcessing = false;
                return;
            }
            updateStreaming(rxString);
            isDataProcessing = false;
            return;
        }

        private grblState grblStateNow = grblState.unknown;
        private grblState grblStateLast = grblState.unknown;
        private void handleStatusMessage(string text, bool probe=false)
        {
            string[] sections = text.Split(',');
            if (probe)              // [PRB:-155.000,-160.000,-28.208:1]
            {   grblStateNow = grblState.probe;
                posProbeOld = posProbe;
                Double.TryParse(sections[0].Substring(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posProbe.X);
                Double.TryParse(sections[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posProbe.Y);
                Double.TryParse(sections[2].Substring(0, sections[2].IndexOf(':')), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posProbe.Z);
                gcodeVariable["PRBX"] = posProbe.X; gcodeVariable["PRBY"] = posProbe.Y; gcodeVariable["PRBZ"] = posProbe.Z;
                gcodeVariable["PRDX"] = posProbe.X- posProbeOld.X; gcodeVariable["PRDY"] = posProbe.Y- posProbeOld.Y; gcodeVariable["PRDZ"] = posProbe.Z- posProbeOld.Z;
            }
            else
            {   // <Idle,MPos:0.000,0.000,0.000,WPos:0.000,0.000,0.000>
                string status = sections[0].Substring(1).Trim(' ');
                Double.TryParse(sections[1].Substring(5), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posMachine.X);
                Double.TryParse(sections[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posMachine.Y);
                Double.TryParse(sections[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posMachine.Z);
                Double.TryParse(sections[4].Substring(5), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posWorld.X);
                Double.TryParse(sections[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posWorld.Y);
                Double.TryParse(sections[6].TrimEnd('>'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out posWorld.Z);
                gcodeVariable["MACX"] = posMachine.X; gcodeVariable["MACY"] = posMachine.Y; gcodeVariable["MACZ"] = posMachine.Z;
                gcodeVariable["WACX"] = posWorld.X;   gcodeVariable["WACY"] = posWorld.Y;   gcodeVariable["WACZ"] = posWorld.Z;
                grblStateNow = parseStatus(status);
            }
            if (grblStateNow != grblStateLast) { grblStateChanged(); }
            if (grblStateNow == grblState.idle)
            {
                waitForIdle = false;
                processSend();
            }
            grblStateLast = grblStateNow;
            OnRaisePosEvent(new PosEventArgs(posWorld, posMachine, grblStateNow));
        }
        private grblState parseStatus(string status)    // {idle, run, hold, home, alarm, check, door}
        {   if (status.IndexOf("Idle") >= 0) { return grblState.idle; }
            if (status.IndexOf("Run") >= 0) { return grblState.run; }
            if (status.IndexOf("Hold") >= 0) { return grblState.hold; }
            if (status.IndexOf("Home") >= 0) { return grblState.home; }
            if (status.IndexOf("Alarm") >= 0) { return grblState.alarm; }
            if (status.IndexOf("Check") >= 0) { return grblState.check; }
            if (status.IndexOf("Door") >= 0) { return grblState.door; }
            return grblState.unknown;
        }
        public event EventHandler<PosEventArgs> RaisePosEvent;
        protected virtual void OnRaisePosEvent(PosEventArgs e)
        {   EventHandler<PosEventArgs> handler = RaisePosEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }


        // check free buffer before sending 
        // 1. requestSend(data) to add data to stack for sending
        // 2. processSend() check if grbl-buffer is free to take commands
        // 3. sendLine(data) if buffer can take commands
        // 4. updateStreaming(rxdata) check if command was sent
        private const int grblBufferSize = 127;  //rx bufer size of grbl on arduino 
        private int grblBufferFree = grblBufferSize;    //actual suposed free bytes on grbl buffer
        private List<string> sendLines= new List<string>();
        private int sendLinesCount;             // actual buffer size
        private int sendLinesSent;              // actual sent line
        private int sendLinesConfirmed;         // already received line
        public void requestSend(string data)
        {
            if ((!string.IsNullOrEmpty(data)) && (data[0] != ';'))//trim lines and remove all empty lines and comment lines
            {
                sendLines.Add(cleanUpCodeLine(data));
                sendLinesCount++;
                processSend();
            }
        }
        private string cleanUpCodeLine(string data)
        {
            var line = data.Replace("\r", "");  //remove CR
            line = line.Replace("\n", "");      //remove LF
            var orig = line;
            int start = orig.IndexOf('(');
            int end = orig.IndexOf(')');
            if (start >= 0) line = orig.Substring(0, start);
            if (end >= 0) line += orig.Substring(end + 1);
            line = line.ToUpper();              //all uppercase
            line = line.Trim();
            return line;
        }
        private bool waitForIdle = false;
        public void processSend()
        {   while ((sendLinesSent < sendLinesCount) && (grblBufferFree >= sendLines[sendLinesSent].Length + 1))
            {
                var line = sendLines[sendLinesSent];
                bool replaced = false;
                if (line.IndexOf('#') > 0)
                {   line = insertVariable(line);
                    replaced = true;
                    if (grblBufferFree < grblBufferSize)        
                        waitForIdle = true;
#if debug_streaming
                    rtbLog.AppendText(string.Format("># {0} {1}\r\n", grblStateNow, grblBufferFree));//if not in transfer log the txLine
                    rtbLog.ScrollToCaret();
#endif
                }
                if (!waitForIdle)
                {   if (replaced)
                        sendLines[sendLinesSent] = line;    // needed to get correct length when receiving 'ok'
                    sendLine(line);
                    grblBufferFree -= (line.Length + 1);
                    sendLinesSent++;
                }
                else
                    return;
            }
        }
        private string insertVariable(string line)
        {
            int pos = 0, posold = 0;
            string variable,mykey = "";
            double myvalue = 0;
            do
            {
                pos = line.IndexOf('@', posold);
                if (pos > 0)
                {
                    myvalue = 0;
                    variable = line.Substring(pos, 5);
                    mykey = variable.Substring(1);
                    if (gcodeVariable.ContainsKey(mykey))
                    { myvalue = gcodeVariable[mykey]; }
                    else { line += " ("+mykey+" not found)"; }
                    line=line.Replace(variable, string.Format("{0:0.000}", myvalue));
                }
                posold = pos+5;
            } while (pos > 0);

            return line.Replace(',','.');
        }
        private void sendLine(string data)
        {   try
            {   serialPort1.Write(data + "\r");
#if debug_streaming
                rtbLog.AppendText(string.Format("> {0} {1} {2} \r\n", data, data.Length+1, grblBufferFree));//if not in transfer log the txLine
                rtbLog.ScrollToCaret();
#else
                if (!(isStreaming&&!isStreamingPause))
                {   rtbLog.AppendText(string.Format("> {0} \r\n",data));//if not in transfer log the txLine
                    rtbLog.ScrollToCaret();
                }
                if (data == "$H")
                {  OnRaisePosEvent(new PosEventArgs(posWorld, posMachine, grblState.home)); }
#endif
            }
            catch (Exception err)
            {   logError("Sending line", err);
                updateControls();
            }
        }


        // Streaming
        // 1. startStreaming() copy and filter gcode to list
        // 2. proceedStreaming() to copy data to stack for sending
        private List<string> gCodeLines;         // buffer with gcode commands
        private List<int> gCodeLineNr;           // corresponding line-nr from main-form
        private int gCodeLinesCount;             // amount of lines to sent
        private int gCodeLinesSent;              // actual sent line
        private int gCodeLinesConfirmed;         // received line
        private bool isStreaming = false;        // true when steaming is in progress
        private bool isStreamingRequestPause = false; // true when request pause (wait for idle to switch to pause)
        private bool isStreamingPause = false;    // true when steaming-pause 
        private bool isStreamingCheck = false;    // true when steaming is in progress (check)
        private bool getParserState = false;      // true to send $G after status switched to idle
        private bool isDataProcessing;      // false when no data processing pending
        private grblStreaming grblStatus = grblStreaming.ok;
        public void stopStreaming()
        {   isStreaming = false;
            if (isStreamingCheck)
            { requestSend("$C"); isStreamingCheck = false; }
        }
        public void pauseStreaming()
        {   if (!isStreamingPause)
            {   isStreamingRequestPause = true;     // wait until buffer is empty before switch to pause
                addToLog("[Pause streaming]");
                addToLog("[Save Settings]");
                grblStatus = grblStreaming.waitidle;
                getParserState = true;
            }
            else
            {   if ((posPause.X != posWorld.X)|| (posPause.Y != posWorld.Y)|| (posPause.Z != posWorld.Z))
                {
                    addToLog("[Restore Position]");
                    requestSend(string.Format("G90 G0 X{0:0.0} Y{1:0.0}", posPause.X, posPause.Y).Replace(',', '.'));  // restore last position
                    requestSend(string.Format("G1 Z{0:0.0}", posPause.Z).Replace(',', '.'));                      // restore last position
                }
                addToLog("[Restore Settings]");
                var cmds = parserState.Split(' ');
                foreach (var cmd in cmds)
                { requestSend(cmd); }           // restore actual GCode settings one by one
                addToLog("[Start streaming - no echo]");
                isStreamingPause = false;
                isStreamingRequestPause = false;
                grblStatus = grblStreaming.ok;
                proceedStreaming();
            }
        }
        public void startStreaming(IList<string> gCodeList, bool check = false)
        {
            rtbLog.Clear();
            addToLog("[Start streaming - no echo]");
            saveLastPos();
            isStreaming = true;
            isStreamingPause = false;
            isStreamingCheck = check;
            grblStatus = grblStreaming.ok;
            if (isStreamingCheck)
                requestSend("$C");
            string[] gCode = gCodeList.ToArray<string>();
            gCodeLinesSent = 0;
            gCodeLinesCount = 0;
            gCodeLinesConfirmed = 0;
            grblBufferFree = grblBufferSize;
            gCodeLines = new List<string>();
            gCodeLineNr = new List<int>();
            sendLinesSent = 0;
            sendLinesCount = 0;
            sendLinesConfirmed = 0;
            sendLines.Clear();
            string tmp;
            for (int i = 0; i < gCode.Length; i++)
            {
                tmp = cleanUpCodeLine(gCode[i]);
                if ((!string.IsNullOrEmpty(tmp)) && (tmp[0] != ';'))//trim lines and remove all empty lines and comment lines
                {
                    gCodeLines.Add(tmp);       // add gcode line to list to send
                    gCodeLineNr.Add(i);         // add line nr
                    gCodeLinesCount++;          // Count total lines
                }
            }
            proceedStreaming();
        }
        private void proceedStreaming()
        {   while ((gCodeLinesSent < gCodeLinesCount) && (grblBufferFree >= gCodeLines[gCodeLinesSent].Length + 1))
            {
                string line = gCodeLines[gCodeLinesSent];
                if (((line == "M0") || (line.IndexOf("M00") >= 0) || (line.IndexOf("M0 ") >= 0)) && !isStreamingCheck)
                {   isStreamingRequestPause = true;
                    addToLog("[Pause streaming]");
                    addToLog("[Save Settings]");
                    grblStatus = grblStreaming.waitidle;
                    getParserState = true;
                    sendStreamEvent(gCodeLineNr[gCodeLinesSent], grblStatus);
//                    requestSend("N0 ");     // send nothing, but expect ok
//                    gCodeLinesSent++;
                    return;                 // abort while - don't fill up buffer
                }
                else
                    requestSend(line);
                gCodeLinesSent++;
            }
        }

        public void updateStreaming(string rxString)
        {   int tmpIndex = gCodeLinesSent;
            if ((rxString != "ok") || (rxString != "[enabled]"))
            {   if (rxString.IndexOf("error") >= 0)
                {   grblStatus = grblStreaming.error;
                    tmpIndex = gCodeLinesConfirmed;
                    addToLog(">> " + rxString);
                }
                if (getParserState)
                {   if (rxString.IndexOf("[G") >= 0)
                    {   parserState = rxString.Substring(1,rxString.Length-2);
                        parserState = parserState.Replace("M0", "");
                        posPause = posWorld;
                        getParserState = false;
                    }
                }
            }
#if debug_streaming
            if (sendLinesConfirmed < sendLinesCount)
                rtbLog.AppendText(string.Format("< {0} {1} {2} {3}\r\n", rxString, sendLines[sendLinesConfirmed], sendLines[sendLinesConfirmed].Length + 1, grblBufferFree));//if not in transfer log the txLine
            else
                rtbLog.AppendText(string.Format("< {0} {1} \r\n", rxString, grblBufferFree));//if not in transfer log the txLine
            rtbLog.ScrollToCaret();
#else
            if (!(isStreaming && !isStreamingPause))
                addToLog(string.Format("< {0}", rxString));
#endif 
            if (sendLinesConfirmed < sendLinesCount)
            {   grblBufferFree += (sendLines[sendLinesConfirmed].Length + 1);   //update bytes supose to be free on grbl rx bufer
                sendLinesConfirmed++;                   // line processed
            }
            if ((sendLinesConfirmed == sendLinesCount)&& (grblStateNow==grblState.idle))   // addToLog(">> Buffer empty\r");
            {   if (isStreamingRequestPause)
                {   isStreamingPause = true;
                    isStreamingRequestPause = false;
                    grblStatus = grblStreaming.pause;
                    gcodeVariable["MLAX"] = posMachine.X; gcodeVariable["MLAY"] = posMachine.Y; gcodeVariable["MLAZ"] = posMachine.Z;
                    gcodeVariable["WLAX"] = posWorld.X; gcodeVariable["WLAY"] = posWorld.Y; gcodeVariable["WLAZ"] = posWorld.Z;

                    if (getParserState)
                    { requestSend("$G"); }
                }
            }
            if (isStreaming)
            {   if (!isStreamingPause)
                    gCodeLinesConfirmed++;  //line processed
                                            //            }
#if debug_streaming
                rtbLog.AppendText(string.Format("< streaming {0} {1} \r\n", gCodeLinesConfirmed, gCodeLinesCount));//if not in transfer log the txLine
#endif
                if (gCodeLinesConfirmed >= gCodeLinesCount)//Transfer finished and processed? Update status and controls
                {   isStreaming = false;
                    addToLog("[Streaming finish]");
                    grblStatus = grblStreaming.finish;
                    if (isStreamingCheck)
                    { requestSend("$C"); isStreamingCheck = false; }
                    updateControls();
                }
                else//not finished
                {   if(!(isStreamingPause||isStreamingRequestPause))
                         proceedStreaming();//If more lines on file, send it  
                }
                if (tmpIndex >= gCodeLinesCount) { tmpIndex = gCodeLinesCount - 1; }
                sendStreamEvent(gCodeLineNr[tmpIndex], grblStatus);
            }
        }
        private void sendStreamEvent(int lineNr, grblStreaming status)
        {
            float codeFinish = (float)gCodeLinesConfirmed * 100 / (float)gCodeLinesCount;
            float buffFinish = (float)(grblBufferSize - grblBufferFree) * 100 / (float)grblBufferSize;
            if (codeFinish > 100) { codeFinish = 100; }
            if (buffFinish > 100) { buffFinish = 100; }
            OnRaiseStreamEvent(new StreamEventArgs((int)lineNr, codeFinish, buffFinish, status));
        }

        private void grblStateChanged()
        {   if ((sendLinesConfirmed == sendLinesCount) && (grblStateNow == grblState.idle))   // addToLog(">> Buffer empty\r");
            {
                if (isStreamingRequestPause)
                {
                    isStreamingPause = true;
                    isStreamingRequestPause = false;
                    grblStatus = grblStreaming.pause;
                    if (getParserState)
                    { requestSend("$G"); }
                }
            }
#if debug_streaming
            rtbLog.AppendText(string.Format(">State changed to {0} \r\n", grblStateNow));
            rtbLog.ScrollToCaret();
#endif
        }

        public event EventHandler<StreamEventArgs> RaiseStreamEvent;
        protected virtual void OnRaiseStreamEvent(StreamEventArgs e)
        {
            EventHandler<StreamEventArgs> handler = RaiseStreamEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        { rtbLog.Clear(); }
        private void tbCommand_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != (char)13) return;
            requestSend(tbCommand.Text);
        }
        private void btnSend_Click(object sender, EventArgs e)
        { requestSend(tbCommand.Text); }
        private void btnGRBLCommand0_Click(object sender, EventArgs e)
        { requestSend("$"); }
        private void btnGRBLCommand1_Click(object sender, EventArgs e)
        { requestSend("$$"); }
        private void btnGRBLCommand2_Click(object sender, EventArgs e)
        { requestSend("$#"); }
        private void btnGRBLCommand3_Click(object sender, EventArgs e)
        { requestSend("$N"); }
        private void btnGRBLCommand4_Click(object sender, EventArgs e)
        { requestSend("$X"); }
        private void btnGRBLReset_Click(object sender, EventArgs e)
        { grblReset(); }

    }
    public enum grblState {idle, run, hold, home, alarm, check, door, probe, unknown};
    public enum grblStreaming{ok,error,reset,finish,pause,waitidle};
    public class StreamEventArgs : EventArgs
    {
        private float codeFinish, buffFinish;
        private int codeLine;
        private grblStreaming status;
        public StreamEventArgs(int c1, float a1, float a2, grblStreaming stat)
        {
            codeLine = c1;
            codeFinish = a1;
            buffFinish = a2;
            status = stat;
        }
        public int CodeLine
        { get { return codeLine; } }
        public float CodeProgress
        {   get { return codeFinish; } }
        public float BuffProgress
        { get { return buffFinish; } }
        public grblStreaming Status
        {   get { return status; } }
    }
    public class PosEventArgs : EventArgs
    {
        private xyzPoint posWorld, posMachine;
        private grblState status;
        public PosEventArgs(xyzPoint world, xyzPoint machine, grblState stat)
        {   posWorld = world;
            posMachine = machine;
            status = stat;
        }
        public xyzPoint PosWorld
        {  get { return posWorld; } }
        public xyzPoint PosMachine
        {  get { return posMachine; } }
        public grblState Status
        {  get { return status; } }
    }

}
