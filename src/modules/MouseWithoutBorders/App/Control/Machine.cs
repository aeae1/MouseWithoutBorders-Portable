// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

// <summary>
//     User control, used in the Matrix form.
// </summary>
// <history>
//     2008 created by Truong Do (ductdo).
//     2009-... modified by Truong Do (TruongDo).
//     2023- Included in PowerToys.
// </history>
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Properties;

namespace MouseWithoutBorders
{
    internal partial class Machine : UserControl
    {
        // private int ip;
        // private Point mouseDownPos;
        private SocketStatus statusClient;

        private SocketStatus statusServer;
        private bool localhost;

        internal Machine()
        {
            InitializeComponent();
            textBoxName.TextChanged += (_, _) => UpdateStatusPresentation();
            Visible = false;
            MachineEnabled = false;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string MachineName
        {
            get => textBoxName.Text;
            set => textBoxName.Text = value;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool MachineEnabled
        {
            get => checkBoxEnabled.Checked;
            set
            {
                checkBoxEnabled.Checked = value;
                Editable = value;
                pictureBoxLogo.Image = value ? Images.MachineEnabled : (System.Drawing.Image)Images.MachineDisabled;
                UpdateStatusPresentation();
                OnEnabledChanged(EventArgs.Empty); // Borrow this event since we do not use it for any other purpose:) (we can create one but l...:))
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        internal bool Editable
        {
            set => textBoxName.Enabled = value;

            // get { return textBoxName.Enabled;  }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        internal bool CheckAble
        {
            set
            {
                if (!value)
                {
                    checkBoxEnabled.Checked = true;
                    Editable = false;
                }

                checkBoxEnabled.Enabled = value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        internal bool LocalHost
        {
            get => localhost;
            set
            {
                localhost = value;
                if (localhost)
                {
                    CheckAble = false;
                }
                else
                {
                    CheckAble = true;
                }

                UpdateStatusPresentation();
            }
        }

        internal void FocusNameEditor()
        {
            _ = textBoxName.Focus();
            textBoxName.SelectAll();
        }

        private void PictureBoxLogo_MouseDown(object sender, MouseEventArgs e)
        {
            // mouseDownPos = e.Location;
            OnMouseDown(e);
        }

        /*
        internal Point MouseDownPos
        {
            get { return mouseDownPos; }
        }
        */

        private void CheckBoxEnabled_CheckedChanged(object sender, EventArgs e)
        {
            MachineEnabled = checkBoxEnabled.Checked;
        }

        private void SetStatusPresentation(string firstLine, string secondLine, Color color)
        {
            labelStatusClient.Text = firstLine;
            labelStatusServer.Text = secondLine;
            labelStatusClient.ForeColor = color;
            labelStatusServer.ForeColor = color;
        }

        private void UpdateStatusPresentation()
        {
            if (localhost)
            {
                SetStatusPresentation("This computer", string.Empty, Color.FromArgb(34, 139, 72));
                return;
            }

            if (!MachineEnabled || string.IsNullOrWhiteSpace(MachineName))
            {
                SetStatusPresentation("Not configured", string.Empty, Color.DimGray);
                return;
            }

            if (statusClient == SocketStatus.InvalidKey || statusServer == SocketStatus.InvalidKey)
            {
                SetStatusPresentation("● Key mismatch", string.Empty, Color.Firebrick);
                return;
            }

            if (statusClient == SocketStatus.Connected || statusServer == SocketStatus.Connected)
            {
                SetStatusPresentation("● Connected", string.Empty, Color.FromArgb(34, 139, 72));
                return;
            }

            if (statusClient is SocketStatus.Resolving or SocketStatus.Connecting or SocketStatus.Handshaking ||
                statusServer is SocketStatus.Resolving or SocketStatus.Connecting or SocketStatus.Handshaking)
            {
                SetStatusPresentation("Connecting…", string.Empty, Color.FromArgb(0, 102, 180));
                return;
            }

            if (statusClient == SocketStatus.Timeout || statusServer == SocketStatus.Timeout)
            {
                SetStatusPresentation("● Timed out", string.Empty, Color.Firebrick);
                return;
            }

            if (statusClient is SocketStatus.Error or SocketStatus.SendError ||
                statusServer is SocketStatus.Error or SocketStatus.SendError)
            {
                SetStatusPresentation("● Connection", "error", Color.Firebrick);
                return;
            }

            if (statusClient == SocketStatus.ForceClosed || statusServer == SocketStatus.ForceClosed)
            {
                SetStatusPresentation("Disconnected", string.Empty, Color.DimGray);
                return;
            }

            SetStatusPresentation("Waiting for", "connection", Color.DimGray);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal SocketStatus StatusClient
        {
            get => statusClient;

            set
            {
                statusClient = value;
                if (statusClient is SocketStatus.Connected or
                    SocketStatus.Handshaking)
                {
                    Editable = false;
                }

                UpdateStatusPresentation();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal SocketStatus StatusServer
        {
            get => statusServer;

            set
            {
                statusServer = value;
                if (statusServer is SocketStatus.Connected or
                    SocketStatus.Handshaking)
                {
                    Editable = false;
                }

                UpdateStatusPresentation();
            }
        }

        private void PictureBoxLogo_Paint(object sender, PaintEventArgs e)
        {
            // e.Graphics.DrawString("(Draggable)", this.Font, Brushes.WhiteSmoke, 20, 15);
        }
    }
}
