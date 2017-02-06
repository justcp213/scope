using System;
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
       Worker picoclass = new Worker();
        public const short ON = 1;
        public const short OFF = 0;
               
    public Form1()
        {
            InitializeComponent();
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

                RunStreaming();

                Task.Factory.StartNew(picoclass.Loop);

            }
            else
            {
                button2status = false;

                //Hier Prozedur stoppen
                picoclass.whileloop = false;
                button2.Text = "Start Streaming";
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
                progress = picoclass.SetChannel(ChA_State, ChB_State, PS2000ACSConsole.Imports.Range.Range_2V);
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
            Init_Device();
        }

        private void RunStreaming()
        {
            //Funktion am 06.02 geschrieben um das Init vom automatischen Streamstart abzukoppeln
            bool progress;
            picoclass.GetTimeInterval();
            progress = picoclass.RunStreaming();
            Console.WriteLine("RunStreaming " + progress);
        }
    }
}
