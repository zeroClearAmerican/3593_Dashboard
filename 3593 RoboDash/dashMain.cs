using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MjpegProcessor;
using System.Globalization;
using ZeroMQ;
using Newtonsoft.Json.Linq;

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

        ZContext context;
        ZSocket requester;
        JObject returnedData = new JObject();

        // Local variables---------------------------------------
        //private readonly string _robotHostname = "roboRIO-3593-FRC.local";
        private readonly string _robotHostname = "roboRIO-3593-FRC.frc-robot.local";
        private readonly string _robotIP = "10.35.93.2";
        private readonly string _piIP = "10.35.93.49";
        //private string _currentBotAddr = "roboRIO-3593-FRC.local";
        private string _currentBotAddr = "roboRIO-3593-FRC.frc-robot.local";
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
            lblMode.Text = "Not Connected";

            //if (context != null && requester != null)
            //{
            //    context.Dispose();
            //    requester.Dispose();
            //}

            context = new ZContext();
            requester = new ZSocket(context, ZSocketType.REQ);
            requester.Connect("tcp://" + _currentBotAddr + ":1180");

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
            AsyncFormUpdate(new Action(() =>
            {
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
                    //stream0.ParseStream(new Uri(_camera0URI));
                }
                catch (Exception)
                {
                    lblStatus.Text = "Could not start camera";
                }
            }));
        }

        private void UpdateLoop(object sender, DoWorkEventArgs e)
        {
            loopActive = true;
            int loopInterval = 50;

            // Loop to get and assign elements, then update UI
            while (loopActive)
            { 
                AsyncFormUpdate(new Action(() =>
                {
                    lblStatus.Text = "Update Loop Running";
                }));

                // Check to see if thread cancellation is pending
                // If true, stop the loop
                if (bwUpdateLoop.CancellationPending)
                {
                    requester.Close();
                    context.Shutdown();
                    context.Dispose();
                    requester.Dispose();
                    loopActive = false;

                    AsyncFormUpdate(new Action(() =>
                    {
                        lblStatus.Text = "Update Loop Stopped";
                    }));
                    break;
                }
                
                /// Socket Code
                // Send
                requester.Send(new ZFrame(_autoMode));

                try
                {
                    // Receive
                    using (ZFrame reply = requester.ReceiveFrame())
                    {
                        returnedData = JObject.Parse(reply.ToString());
                    }
                }
                catch (ZException ex)
                {
                    requester.Close();
                    context.Shutdown();
                    context.Dispose();
                    requester.Dispose();
                    loopActive = false;

                    AsyncFormUpdate(new Action(() =>
                    {
                        lblMode.Text = "Loop Stopped";
                        lblStatus.Text = "Update Loop Stopped";
                    }));
                    break;
                }

                // ======================================================

                // Take all elements from the Dictionary and update the UI with them
                AsyncFormUpdate(new Action(() => ShowUI(returnedData)));

                // Stall the thread for a given interval to save on bandwidth
                Thread.Sleep(loopInterval);
            }

            /// update the UI with this status
            AsyncFormUpdate(new Action(() => {
                lblMode.Text = "Not Connected";
            }));
        }

        // When a new frame is received, display it in the UI, viewCam0
        private void FrameReady_cam0(object sender, FrameReadyEventArgs e)
        {
            AsyncFormUpdate(new Action(() => viewCam0.Image = e.Bitmap));
            // CHECK IF CAMERA VIEW HAS CHANGED

            if (!returnedData.ContainsKey("cameraView")) return;

            if (returnedData["cameraView"].ToString() == ((CameraPort)currentCameraView).ToString()) return;
            
            // If the cameraView in NT has changed, switch camera views and restart camera
            switch(returnedData["cameraView"].ToString())
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
        private void ShowUI(JObject stat)
        {
            // Set UI element values to their respective elements in the dictionary
            /// Robot Mode
            lblMode.Text = stat["robotMode"].ToString();

            /// Power Values
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
            double IA_A = Convert.ToDouble(stat["IntakeLeft"]) + Convert.ToDouble(stat["IntakeRight"]);
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


            /// Battery Voltage
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

            
            /// Pneumatic Pressure
            lblPressure.Text = stat["systemPressure"] + " PSI";
            if (Convert.ToDouble(stat["systemPressure"]) > 100)
            {
                lblPressure.BackColor = Color.LimeGreen; // Green Pressure
                lblPressure.ForeColor = Color.Silver;
            }
            else if (Convert.ToDouble(stat["systemPressure"]) <= 100 && Convert.ToDouble(stat["systemPressure"]) > 60)
            {
                lblPressure.BackColor = Color.Gold; // Yellow Pressure
                lblPressure.ForeColor = Color.Black;
            }
            else
            {
                lblPressure.BackColor = Color.Maroon; // Red Pressure
                lblPressure.ForeColor = Color.Silver;
            }


            /// Gyro
            if (Math.Abs(Convert.ToDouble(stat["gyroAngle"])) < 360)
            {
                // If angle is less than zero, indicator should show the angle from 0 backwards
                if (Convert.ToDouble(stat["gyroAngle"]) < 0)
                {
                    double angle = 360 - Convert.ToDouble(stat["gyroAngle"]);
                    cgGyroAngle.Value = (int)angle;
                }
                else
                    cgGyroAngle.Value = Convert.ToInt32(stat["gyroAngle"]);
            }
            cgGyroAngle.Text = stat["gyroAngle"].ToString();


            /// Encoders
            double encoderAverage = (Convert.ToDouble(stat["driveLeftEncoder"]) + Convert.ToDouble(stat["driveRightEncoder"])) / 2;
            lblEncDelta.Text = encoderAverage.ToString("F", CultureInfo.InvariantCulture);


            /// Shifter
            bool shifter = Convert.ToBoolean(stat["driveShifter"].ToString());
            if (shifter)
            {
                lblShifter.Text = "HIGH";
                lblShifter.BackColor = Color.LimeGreen;
            }
            else
            {
                lblShifter.Text = "LOW";
                lblShifter.BackColor = Color.SteelBlue;
            }


            /// Intake Arms
            bool arms = Convert.ToBoolean(stat["intakeArms"].ToString());
            if (arms)
            {
                lblArmPos.Text = "OUT";
                lblArmPos.BackColor = Color.LimeGreen;
            }
            else
            {
                lblArmPos.Text = "IN";
                lblArmPos.BackColor = Color.Firebrick;
            }


            ///Shifter
            bool lifter = Convert.ToBoolean(stat["shooterPosition"].ToString());
            if (lifter)
            {
                lblLifter.Text = "SWITCH";
                lblLifter.BackColor = Color.Blue;
            }
            else
            {
                lblLifter.Text = "SCALE";
                lblLifter.BackColor = Color.LimeGreen;
            }


            ///Shifter
            bool flap = Convert.ToBoolean(stat["flapPosition"].ToString());
            if (flap)
            {
                lblFlapPos.Text = "UP";
                lblFlapPos.BackColor = Color.LimeGreen;
            }
            else
            {
                lblFlapPos.Text = "DOWN";
                lblFlapPos.BackColor = Color.Firebrick;
            }


        } // Updates all UI elements with new status from the robot

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
            string[] elements;

            Pen whitePen = new Pen(Color.White); // White lines
            whitePen.Width = 2.0F;
            Pen redPen = new Pen(Color.Red); // redPen 
            redPen.Width = 1.0F;
            Pen bluePen = new Pen(Color.RoyalBlue); // bluePen
            bluePen.Width = 1.0F;
            Pen steelBluePen = new Pen(Color.SteelBlue); // steelBluePen
            steelBluePen.Width = 1.0F;
            Pen limeGreenPen = new Pen(Color.LimeGreen); // limeGreenPen
            limeGreenPen.Width = 1.0F;

            // Robot
            elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\Graphics\\robot.graph"); // Read powerlines file
            foreach (string s in elements)
            {
                if (s.Replace(" ", "") == "") continue;
                string[] line = s.Split(' ');
                g.DrawRectangle(whitePen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
            }

            // Power lines
            elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\Graphics\\powerLines.graph"); // Read powerlines file
            foreach (string s in elements)
            {
                string[] line = s.Split(' ');
                switch (line[0].ToLower())
                {
                    case "line":
                        g.DrawLine(redPen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "dot":
                        g.FillEllipse(new SolidBrush(redPen.Color), Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), 6, 6);
                        break;
                }
            }

            // Lifter
            elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\Graphics\\lifter.graph"); // Read powerlines file
            foreach (string s in elements)
            {
                string[] line = s.Split(' ');
                switch (line[0].ToLower())
                {
                    case "line":
                        g.DrawLine(bluePen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "dot":
                        g.FillEllipse(new SolidBrush(bluePen.Color), Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), 6, 6);
                        break;
                }
            }

            // Shifter
            elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\Graphics\\shifter.graph"); // Read powerlines file
            foreach (string s in elements)
            {
                string[] line = s.Split(' ');
                switch (line[0].ToLower())
                {
                    case "line":
                        g.DrawLine(steelBluePen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "dot":
                        g.FillEllipse(new SolidBrush(steelBluePen.Color), Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), 6, 6);
                        break;
                }
            }

            // Intake Arms
            elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\Graphics\\intakeArms.graph"); // Read powerlines file
            foreach (string s in elements)
            {
                string[] line = s.Split(' ');
                switch (line[0].ToLower())
                {
                    case "line":
                        g.DrawLine(limeGreenPen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "dot":
                        g.FillEllipse(new SolidBrush(limeGreenPen.Color), Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), 6, 6);
                        break;
                }
            }

            // Flap
            elements = System.IO.File.ReadAllLines(Environment.CurrentDirectory + "\\Graphics\\flap.graph"); // Read powerlines file
            foreach (string s in elements)
            {
                string[] line = s.Split(' ');
                switch (line[0].ToLower())
                {
                    case "line":
                        g.DrawLine(redPen, Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), Convert.ToInt32(line[3]), Convert.ToInt32(line[4]));
                        break;
                    case "dot":
                        g.FillEllipse(new SolidBrush(redPen.Color), Convert.ToInt32(line[1]), Convert.ToInt32(line[2]), 6, 6);
                        break;
                }
            }

            bluePen.Dispose();
            steelBluePen.Dispose();
            limeGreenPen.Dispose();
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

        private void RobotAddressChanged(object sender, EventArgs e)
        {
            // Set the program's IP values to the robot's IP address instead of using mDNS, then restart everything
            _currentBotAddr = txtRobotAddress.Text;
            _currentCameraHost = txtRobotAddress.Text;

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
            stream0.StopStream();
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

        private void txtRobotAddress_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return)
                RobotAddressChanged(txtRobotAddress, EventArgs.Empty);
        }

        private void AsyncFormUpdate(Action action)
        {
            if (action == null) return;
            // Do any pending events before doing our form update
            //Application.DoEvents();

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
