using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using NetworkTables;
using MjpegProcessor;
using System.Globalization;

namespace _3593_RoboDash
{
    public partial class DashMain : Form
    {
        // UI ---------------------------------------------------
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        //This code allows for the form to be moved on a click-down event without the use of a border
         

        Point formLocation = new Point(0, 30);

        BackgroundWorker bwUpdateLoop;
        bool loopActive = true;

        public static NetworkTable ntPower;
        public static NetworkTable ntBehavior;
        public static NetworkTable ntValues;
        public static NetworkTable ntVision;

        // Local variables---------------------------------------
        private readonly string _robotHostname = "roboRIO-3593-FRC.local";
        private readonly string _robotIP = "10.35.93.2";
        private readonly string _piIP = "10.35.93.49";
        private string _currentBotAddr = "roboRIO-3593-FRC.local";
        private string _autoMode = "BASEONLY";

        enum CameraPort : int
        {
            FRONT = 1185,
            REAR = 1188,
            VISION = 1187
        }
        private string _camera0URI;
        private string _currentCameraHost = "10.35.93.49";
        private int currentCameraView = (int)CameraPort.FRONT;
        MjpegDecoder stream0;

        static List<Label> autoLabels;

        public DashMain()
        {
            // Set the form's start position to the top-left before the form is displayed
            this.StartPosition = FormStartPosition.Manual;
            this.Location = formLocation;
            _camera0URI = "http://" + _currentCameraHost + ":" + currentCameraView + "/?action=stream";

            InitializeComponent();
            lblStatus.Text = "";

            autoLabels = new List<Label>()
            {
                btnAutoBaseline,
                btnAutoLeft, 
                btnAutoMiddle,
                btnAutoRight
            };

            // Begin the main updater loop, which also starts the camera
            StartUpdateLoop();
        }

        // Begins a new thread to get the data and update UI controls
        private void StartUpdateLoop()
        {
            NetworkTable.Shutdown();
            UpdateMode("Not Connected");

            // Initialize NetworkTables objects and set this program to NT clientMode
            NetworkTable.SetClientMode();
            // Use mDNS only in testing. In competition, use the actual IP
            NetworkTable.SetIPAddress(_currentBotAddr);
            NetworkTable.SetTeam(3593);
            NetworkTable.SetNetworkIdentity("3593-Dashboard");
            NetworkTable.Initialize();

            // Initialize network tables
            ntPower = NetworkTable.GetTable("3593-Power");
            ntValues = NetworkTable.GetTable("3593-Values");
            ntBehavior = NetworkTable.GetTable("3593-Behavior");
            ntBehavior.PutString("autoMode", _autoMode); // Immediately set autoMode to make sure it's in the NT

            // Get camera URI and start the camera feed
            StartCamera();

            // start the main updater loop under a backgroundworker
            bwUpdateLoop = new BackgroundWorker
            {
                WorkerSupportsCancellation = true,
                WorkerReportsProgress = false
            };
            bwUpdateLoop.DoWork += UpdateLoop;
            bwUpdateLoop.RunWorkerAsync();
        } 

        private void StartCamera()
        {
            // Stop the current stream if it's running
            if (stream0 != null)
            {
                stream0.StopStream();
                viewCam0.Image = viewCam0.ErrorImage;
            }

            // Delay 100ms, then screate a new stream URL and connect
            Thread.Sleep(100);
            stream0 = new MjpegDecoder();
            stream0.FrameReady += FrameReady_cam0;
            _camera0URI = "http://" + _currentCameraHost + ":" + currentCameraView + "/?action=stream";

            try
            {
                stream0.ParseStream(new Uri(_camera0URI));
            }
            catch (Exception)
            {
                lblStatus.Text = "Could not start camera";
            }
        }

        private void UpdateLoop(object sender, DoWorkEventArgs e)
        {
            // Build Dictionary of elements to get
            Dictionary<string, string> elements = BuildUIDict();

            loopActive = true;
            int loopInterval = 50;

            // Loop to get and assign elements, then update UI
            while (loopActive)
            { 
                AsyncFormUpdate(new Action(() =>
                {
                    lblStatus.Text = ntBehavior.IsConnected ? "NT Connected" : "NT Failed";
                    lblStatus.Text = "Update Loop Running";
                }));

                // Check to see if thread cancellation is pending
                // If true, stop the loop
                if (bwUpdateLoop.CancellationPending)
                {
                    AsyncFormUpdate(new Action(() =>
                    {
                        lblStatus.Text = "Update Loop Stopped";
                    }));
                    break;
                }

                // Get all information from the NetworkTables ===========

                // Behavior
                elements["robotMode"] = ntBehavior.GetString("robotMode", "Not Connected");

                // Values
                /// Pneumatics

                elements["systemPressure"] = ntValues.GetNumber("systemPressure", 0).ToString();
                elements["intakeArms"] = ntValues.GetBoolean("intakeArms", false).ToString();
                elements["shooterPosition"] = ntValues.GetBoolean("shooterPosition", true).ToString();
                elements["driveShifter"] = ntValues.GetBoolean("driveShifter", true).ToString();
                elements["flapPosition"] = ntValues.GetBoolean("flapPosition", true).ToString();

                /// Power
                elements["driveLeft1"] = ntPower.GetNumber("driveLeft1", 0).ToString();
                elements["driveLeft2"] = ntPower.GetNumber("driveLeft2", 0).ToString();
                elements["driveRight1"] = ntPower.GetNumber("driveRight1", 0).ToString();
                elements["driveRight2"] = ntPower.GetNumber("driveRight2", 0).ToString();
                elements["IntakeLeft"] = ntPower.GetNumber("IntakeLeft", 0).ToString();
                elements["IntakeRight"] = ntPower.GetNumber("IntakeRight", 0).ToString();
                elements["cimmy1"] = ntPower.GetNumber("cimmy1", 0).ToString();
                elements["cimmy2"] = ntPower.GetNumber("cimmy2", 0).ToString();
                elements["shooterLeft1"] = ntPower.GetNumber("shooterLeft1", 0).ToString();
                elements["shooterLeft2"] = ntPower.GetNumber("shooterLeft2", 0).ToString();
                elements["shooterRight1"] = ntPower.GetNumber("shooterRight1", 0).ToString();
                elements["shooterRight2"] = ntPower.GetNumber("shooterRight2", 0).ToString();
                //elements["pdpPCMCurrent"] = nt_values.GetNumber("pdpPCMCurrent", 0).ToString();
                elements["totalCurrent"] = ntPower.GetNumber("totalCurrent", 0).ToString();
                elements["battVoltage"] = ntPower.GetNumber("battVoltage", 0).ToString();

                // Sensors
                elements["driveLeftEncoder"] = ntValues.GetNumber("driveLeftEncoder", 0).ToString();
                elements["driveRightEncoder"] = ntValues.GetNumber("driveRightEncoder", 0).ToString();
                elements["gyroAngle"] = ntValues.GetNumber("gyroAngle", 0).ToString();
                elements["gyroPIDRotation"] = ntValues.GetNumber("gyroPIDRotation", 0).ToString();
                elements["rotationCorrection"] = ntValues.GetNumber("rotationCorrection", 0).ToString();

                // Vision
                elements["targetsFound"] = ntValues.GetNumber("targetsFound", 0).ToString();

                // ======================================================

                // Take all elements from the Dictionary and update the UI with them
                AsyncFormUpdate(new Action(() => ShowUI(elements)));

                // Stall the thread for a given interval to save on bandwidth
                Thread.Sleep(loopInterval);
            }

            /// update the UI with this status
            UpdateMode("Not Connected");
        }

        // Build the dictionary of values that will be taken from the updater loop and displayed in the UI
        private Dictionary<string, string> BuildUIDict()
        {
            Dictionary<string, string> elements = new Dictionary<string, string>
            {

                // Behaviour-----------
                { "robotMode", "Not Connected" },

                // Values--------------

                /// Power-----
                { "driveRight1", "0" },
                { "driveRight2", "0" },
                { "driveLeft1", "0" },
                { "driveLeft2", "0" },
                { "IntakeLeft", "0" },
                { "IntakeRight", "0" },
                { "cimmy1", "0" },
                { "cimmy2", "0" },
                { "shooterLeft1", "0" },
                { "shooterLeft2", "0" },
                { "shooterRight1", "0" },
                { "shooterRight2", "0" },
                { "pdpPCMCurrent", "0" },
                { "totalCurrent", "0" },
                { "battVoltage", "0" },

                /// Pneumatics
                { "driveShifter", "Low" },
                { "shooterPosition", "Up" },
                { "intakeArms", "In" },
                { "flapPosition", "Down" },
                { "systemPressure", "0" },

                // Sensors-------------
                { "driveLeftEncoder", "0" },
                { "driveRightEncoder", "0" },
                { "rotationCorrection", "0" },
                { "gyroAngle", "0" },
                { "gyroPIDRotation", "0" },

                // Vision--------------
                { "targetsFound", "0" },
            };

            return elements;
        }

        // When a new frame is received, display it in the UI, viewCam0
        private void FrameReady_cam0(object sender, FrameReadyEventArgs e)
        {
            AsyncFormUpdate(new Action(() => viewCam0.Image = e.Bitmap));
            // CHECK IF CAMERA VIEW HAS CHANGED
            //if (!ntBehavior.IsConnected) return;

            if (ntBehavior.GetString("cameraView", "FRONT") == ((CameraPort)currentCameraView).ToString()) return;
            
            // If the cameraView in NT has changed, switch camera views and restart camera
            switch(ntBehavior.GetString("cameraView", "FRONT"))
            {
                case "REAR":
                    currentCameraView = (int)CameraPort.REAR;
                    break;
                case "FRONT":
                default:
                    currentCameraView = (int)CameraPort.FRONT;
                    break;
            }
            StartCamera();
        }

        #region UI Processsing

        delegate void SetTextCallback(Dictionary<string, string> input);
        private void ShowUI(Dictionary<string, string> stat)
        {
            AsyncFormUpdate(new Action(() =>
            {
                // Set UI element values to their respective elements in the dictionary
                lblMode.Text = stat["robotMode"];

                // Drive Left
                double DL_A = Convert.ToDouble(stat["driveLeft1"]) + Convert.ToDouble(stat["driveLeft2"]);
                lblDriveLeftCurrent.Text = DL_A + "A";
                gradientDriveLeft.Width = Convert.ToInt32((1 - (DL_A / 100f)) * 379f) - 5;
                gradientDriveLeft.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (DL_A / 100f)) * 379f)), gradientDriveLeft.Location.Y);

                // Drive Right
                double DR_A = Convert.ToDouble(stat["driveRight1"]) + Convert.ToDouble(stat["driveRight2"]);
                lblDriveRightCurrent.Text = DR_A + "A";
                gradientDriveRight.Width = Convert.ToInt32((1 - (DR_A / 100f)) * 379f) - 5;
                gradientDriveRight.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (DR_A / 100f)) * 379f)), gradientDriveRight.Location.Y);

                // Summed Intakes 
                double IA_A = Convert.ToDouble(stat["IntakeLeft"]) + Convert.ToDouble(stat["IntakeLeft"]);
                lblArmIntakeCurrent.Text = IA_A + "A";
                gradientArmIntakes.Width = Convert.ToInt32((1 - (IA_A / 100f)) * 379f) - 5;
                gradientArmIntakes.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (IA_A / 100f)) * 379f)), gradientArmIntakes.Location.Y);

                // Summed Cimmys
                double CI_A = Convert.ToDouble(stat["cimmy1"]) + Convert.ToDouble(stat["cimmy1"]);
                lblCimmyCurrent.Text = CI_A + "A";
                gradientCimmyIntakes.Width = Convert.ToInt32((1 - (CI_A / 100f)) * 379f) - 5;
                gradientCimmyIntakes.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (CI_A / 100f)) * 379f)), gradientCimmyIntakes.Location.Y);

                //// Power Shooters
                double PS_A = Convert.ToDouble(stat["shooterRight2"]) + Convert.ToDouble(stat["shooterLeft2"]);
                lblPowerShooterCurrent.Text = PS_A + "A";
                gradientPowerShooters.Width = Convert.ToInt32((1 - (PS_A / 100f)) * 379f) - 5;
                gradientPowerShooters.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (PS_A / 100f)) * 379f)), gradientPowerShooters.Location.Y);

                // Speed Shooters
                double SS_A = Convert.ToDouble(stat["shooterRight1"]) + Convert.ToDouble(stat["shooterLeft1"]);
                lblSpeedShooterCurrent.Text = SS_A + "A";
                gradientSpeedShooters.Width = Convert.ToInt32((1 - (SS_A / 100f)) * 379f) - 5;
                gradientSpeedShooters.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (SS_A / 100f)) * 379f)), gradientSpeedShooters.Location.Y);

                // Total Current
                double totalA = Convert.ToDouble(stat["totalCurrent"]);
                lblTotalCurrent.Text = stat["totalCurrent"] + "A";
                gradTotalCurrent.Width = Convert.ToInt32((1 - (totalA / 100f)) * 379f) - 5;
                gradTotalCurrent.Location = new Point(pictureBox4.Right + 5 -
                    (Convert.ToInt32((1 - (totalA / 100f)) * 379f)), gradTotalCurrent.Location.Y);

                // Battery Voltage
                lblVoltMeter.Text = stat["battVoltage"] + "V";
                if (Convert.ToDouble(stat["battVoltage"]) > 12.2)
                {
                    lblVoltMeter.BackColor = Color.LimeGreen; // Green Battery
                    lblVoltMeter.ForeColor = Color.Silver;
                }
                else if (Convert.ToDouble(stat["battVoltage"]) <= 12.2 && Convert.ToDouble(stat["battVoltage"]) > 11)
                {
                    lblVoltMeter.BackColor = Color.Gold; // Yellow Battery
                    lblVoltMeter.ForeColor = Color.Black;
                }
                else
                {
                    lblVoltMeter.BackColor = Color.Maroon; // Red Battery
                    lblVoltMeter.ForeColor = Color.Silver;
                }

                // System Pressure
                // Battery Voltage
                lblPressure.Text = stat["systemPressure"] + " PSI";
                if (Convert.ToDouble(stat["systemPressure"]) > 100)
                {
                    lblPressure.BackColor = Color.LimeGreen; // Green Battery
                    lblPressure.ForeColor = Color.Silver;
                }
                else if (Convert.ToDouble(stat["systemPressure"]) <= 100 && Convert.ToDouble(stat["systemPressure"]) > 60)
                {
                    lblPressure.BackColor = Color.Gold; // Yellow Battery
                    lblPressure.ForeColor = Color.Black;
                }
                else
                {
                    lblPressure.BackColor = Color.Maroon; // Red Battery
                    lblPressure.ForeColor = Color.Silver;
                }

                if (Convert.ToDouble(stat["gyroAngle"]) < 0)
                {
                    double angle = 360 - Convert.ToDouble(stat["gyroAngle"]);
                    cgGyroAngle.Value = (int)angle;
                }
                else
                    cgGyroAngle.Value = Convert.ToInt32(stat["gyroAngle"]);

                lblEncDelta.Text = (Convert.ToDouble(stat["driveLeftEncoder"]) - Convert.ToDouble(stat["driveRightEncoder"])).ToString("F", CultureInfo.InvariantCulture);
                lblGyroPID.Text = stat["gyroAngle"];
            }));
        } // Updates all UI elements with new status from the robot

        delegate void SetStringCallback(string input);
        private void UpdateMode(string s)
        {
            if (lblMode.InvokeRequired)
            {
                SetStringCallback d = new SetStringCallback(UpdateMode);
                try
                {
                    Invoke(d, new object[] { s });
                }
                catch (ObjectDisposedException)
                {
                    // This occurs when a control is not active but we're still attempting to write to it
                }
            }
            else
                lblMode.Text = s;
        } // This method updates just the robotMode UI element

        #endregion

        #region UI Events

        /// <summary>
            /* All of the points that make up the lines, rectangles, and dots are loaded from a file called "graphics.txt" in the same
             * directory as the executable. Each element is on its own line, where each property of each element is separated by a space.
             * The syntax of each valid element is outlined below:
             * 
             * Rectangle:
             * rect X Y WIDTH HEIGHT
             *
             * Line:
             * line X1 Y1 X2 Y2
             *
             * Dot: (locked to 6x6)
             * dot X Y
             *
             * Any line that begins with anything other than a valid directive is ignored and can be used like a comment
             */
        /// </summary>
        public void DrawGraphics(PaintEventArgs e)
        {
            Graphics g = e.Graphics; // Graphics Engine
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            Pen whitePen = new Pen(Color.White); // White lines
            whitePen.Width = 2.0F;
            Pen redPen = new Pen(Color.Red); // Red 
            redPen.Width = 1.0F;
            //g.FillRectangle(new SolidBrush(Color.FromArgb(74, 74, 74)), 1305, 118, 252, 252);
            //g.DrawEllipse(redPen, 1307, 120, 248, 248);

            string[] elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\graphics.txt"); // Read elements file

            // For each line in the file, create a new element if applicable
            foreach (string s in elements)
            {
                string[] line = s.Split(' ');
                switch (line[0].ToLower())
                {
                    case "rect":
                        g.DrawRectangle(whitePen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "line":
                        g.DrawLine(redPen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "dot":
                        g.FillEllipse(new SolidBrush(redPen.Color), Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), 6, 6);
                        break;
                    case "circle":
                        //g.DrawEllipse(redPen, 1305, 118, 252, 252);
                        break;
                    case default(string):
                        break;
                }
            }

            whitePen.Dispose();
            redPen.Dispose();
            g.Dispose();
        } // Draw all graphics elements

        private void btnRestartConnnection_Click(object sender, EventArgs e)
        {
            if (bwUpdateLoop != null)
            {
                loopActive = false;
                bwUpdateLoop.CancelAsync();
                bwUpdateLoop.Dispose();
                lblMode.Text = "Not Connected";
            }

            StartUpdateLoop();
        } // Restart the entire update loop

        private void button1_Click(object sender, EventArgs e)
        {
            // Set the program's IP values to the robot's IP address instead of using mDNS, then restart everything
            if (_currentBotAddr != _robotIP)
            {
                _currentBotAddr = _robotIP;
                _currentCameraHost = _robotIP;
                btnFieldMode.Text = "Switch to Testing Mode";
                lblIPMode.Text = "Field Mode active";
            }
            else if (_currentBotAddr != _robotHostname)
            {
                _currentBotAddr = _robotHostname;
                _currentCameraHost = _robotHostname;
                btnFieldMode.Text = "Switch to Field Mode";
                lblIPMode.Text = "Testing Mode active";
            }

            if (bwUpdateLoop != null)
            {
                loopActive = false;
                bwUpdateLoop.CancelAsync();
                lblMode.Text = "Not Connected";
            }

            StartUpdateLoop();
        } // Switches from Field Mode to Testing Mode

        private void button1_Click_1(object sender, EventArgs e)
        {
            // This method is most useful when working on the UI graphics, as you can make changes in "graphics.txt" 
            // and use this method to view the changes without stopping and rebuilding the code each time.
            this.Refresh();
        } // Re-paint UI Graphics

        private void lblMode_TextChanged(object sender, EventArgs e)
        {
            var lbl = sender as Label;
            lbl.ForeColor = (lbl.Text == "Not Connected") ? Color.Red : Color.LawnGreen;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            loopActive = false;
            
            bwUpdateLoop?.CancelAsync();
            Close();
        } // Closes form

        private void btnMinimize_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        } // Minimizes form

        private void dashMain_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        } // Allow the form to be moved around without the border

        private void btnRestartCamera_Click(object sender, EventArgs e)
        {
            StartCamera();
        } // Button handler for restarting the camera feed

        private void dashMain_Paint(object sender, PaintEventArgs e) // When the form is rendered, draw the robot and line graphics
        {
            DrawGraphics(e);
        }

        private void lblCameraReset_MouseEnter(object sender, EventArgs e)
        {
            btnCameraReset.BackColor = Color.FromArgb(54, 54, 54);
        }

        private void lblCameraReset_MouseLeave(object sender, EventArgs e)
        {
            btnCameraReset.BackColor = Color.FromArgb(34, 34, 34);
        }

        private void lblCameraReset_MouseDown(object sender, MouseEventArgs e)
        {
            lblCameraReset_MouseLeave(sender, e);
        }

        private void lblCameraReset_MouseUp(object sender, MouseEventArgs e)
        {
            lblCameraReset_MouseEnter(sender, e);
        }

        private void lblRestartDash_MouseEnter(object sender, EventArgs e)
        {
            btnRestartDash.BackColor = Color.FromArgb(54, 54, 54);
        }

        private void lblRestartDash_MouseLeave(object sender, EventArgs e)
        {
            btnRestartDash.BackColor = Color.FromArgb(34, 34, 34);
        }

        private void lblRestartDash_MouseDown(object sender, MouseEventArgs e)
        {
            lblRestartDash_MouseLeave(sender, e);
        }

        private void lblRestartDash_MouseUp(object sender, MouseEventArgs e)
        {
            lblRestartDash_MouseEnter(sender, e);
        }

        private void autoButtonMouseEnter(object sender, EventArgs e)
        {
            (sender as Label).BackColor = Color.Firebrick;
        }

        private void autoButtonMouseLeave(object sender, EventArgs e)
        {
            var lbl = sender as Label;
            if (lblActiveAuto.Text != lbl.Text)
            {
                lbl.BackColor = Color.DarkRed;
                lbl.ForeColor = Color.Silver;
            }
        }

        private void CameraSelectionChanged(object sender, EventArgs e)
        {
            var rad = sender as RadioButton;

            switch (rad.Text)
            {
                case "Front Camera":
                    currentCameraView = (int)CameraPort.FRONT;
                    _currentCameraHost = _piIP; // Raw camera feed from the pi
                    StartCamera();
                    break;
                case "Vision Stream":
                    currentCameraView = (int)CameraPort.VISION;
                    _currentCameraHost = "127.0.0.1"; // Localhost camera processing 
                    StartCamera();
                    break;
                case "Rear Camera":
                    currentCameraView = (int)CameraPort.REAR;
                    _currentCameraHost = _currentBotAddr; // Rear camera on RoboRio
                    StartCamera();
                    break;
                default:
                    break;
                
            }
        }

        private void AsyncFormUpdate(Action action)
        {
            if (action == null) return;
            // Do any pending events before doing our form update
            Application.DoEvents();

            // Execute the action on the form UI thread
            if (this.InvokeRequired)
                this.BeginInvoke(action, null);
            else
                action();
        }

        #endregion

        #region Autonomous buttons
        private void AutoButtonClick(string s)
        {
            lblActiveAuto.Text = s.ToUpper();
            this._autoMode = s.ToUpper();
            ntBehavior.PutString("autoMode", _autoMode);
        }

        private void AutoButtonDown(object sender, EventArgs e)
        {
            var lbl = sender as Label;
            lbl.BackColor = Color.Red;
            lbl.ForeColor = Color.Black;

            AutoButtonClick(lbl.Text);

            foreach(var l in autoLabels)
            {
                if (l != lbl)
                {
                    l.BackColor = Color.DarkRed;
                    l.ForeColor = Color.Silver;
                }
            }
        }

        #endregion
        
    }
}
