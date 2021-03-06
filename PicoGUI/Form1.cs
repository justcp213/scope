﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;



namespace PicoGUI
{


    public partial class Form1 : Form
    {
        public bool button2status = false;
        bool device_init = false;
        Worker picoclass;
        public const short ON = 1;
        public const short OFF = 0;
        System.Diagnostics.Stopwatch Stopwatch = new System.Diagnostics.Stopwatch();
        PS2000ACSConsole.Imports.Range Vrange;

        Label[] labelindex = new Label[2];

        public Form1()
        {
            picoclass = new Worker(this);
            InitializeComponent();
            VoltageComboBox.SelectedIndex = 5;
            CouplingComboBox.SelectedIndex = 1;
            labelindex[0] = abtastintervall_label ;
            labelindex[1] = Abtastrate_label;
    }

        private void button2_Click(object sender, EventArgs e)
        {
            if (!button2status)
            {
                button2status = true;
                picoclass.whileloop = true;
                button2.Text = "Stop Streaming";
                //Hier Prozedur anfangen
                if (!device_init)
                    Init_Device();
                Stopwatch.Start();
                timer1.Enabled = true;
                
                RunStreaming();

                Task.Factory.StartNew(picoclass.Loop);

            }
            else
            {
                Stopwatch.Stop();
                timer1.Enabled = false;
                button2status = false;

                //Hier Prozedur stoppen
                picoclass.whileloop = false;
                button2.Text = "Start Streaming";
                Stopwatch.Reset();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "log files (*.log)|*.log|All files (*.*)|*.*";
            if(sfd.ShowDialog() == DialogResult.OK) { 
          
                picoclass.destinationfile = sfd.FileName + ".log";
                button2.Enabled = true;
            }
            else
                MessageBox.Show("Es wurde kein korrekter Pfad gewählt", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.ServiceNotification);
        }


        private void Init_Device()
        {
            short ChA_State = ON;   
            short ChB_State = OFF;

            bool progress;

            progress = picoclass.InitPS2000A();
            Console.WriteLine("InitPs2000A: " + progress);
            if (progress) { 
                progress = picoclass.SetChannel(ChA_State, ChB_State, Vrange);
                Console.WriteLine("SetChannel " + progress);

                if (progress) { 
                                progress = picoclass.SetDataBuffer();
                                Console.WriteLine("SetDataBuffer " + progress);
                }
                if (progress)
                {
                    button3.Enabled = false;
                    button2.Enabled = true;
                }
            }
            //picoclass.GetTimeInterval();
            //progress = picoclass.RunStreaming();
            //Console.WriteLine("RunStreaming " + progress);

            
            device_init = true;
      
        }

        private void button3_Click(object sender, EventArgs e)
        {
            GetSettings();
            Init_Device();
            picoclass.GetTimeInterval();
        }

        private void GetSettings()
        {

            Vrange = (PS2000ACSConsole.Imports.Range)(VoltageComboBox.SelectedIndex+2);
            picoclass.coupling_type = (short)CouplingComboBox.SelectedIndex;

            picoclass.VRange = Vrange;


        }

        private void RunStreaming()
        {
            //Funktion am 06.02 geschrieben um das Init vom automatischen Streamstart abzukoppeln
            bool progress;
           // picoclass.GetTimeInterval();
            progress = picoclass.RunStreaming();
            Console.WriteLine("RunStreaming " + progress);
        }

        public void SetLabels(string text, int labelno)
        {
            //0 abtastintervall
            //1 abtastrate
            if (labelindex[labelno].InvokeRequired)
            {
                Invoke(new MethodInvoker(() => labelindex[labelno].Text = text));

            }
            else
                labelindex[labelno].Text = text;

        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            TimerLabel.Text = Stopwatch.ElapsedMilliseconds.ToString();
        }
    }
}
