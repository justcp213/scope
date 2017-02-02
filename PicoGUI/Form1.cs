using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;



namespace PicoGUI
{
   
    
    public partial class Form1 : Form
    {
       public bool button2status = false;
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
                Thread_program();
            }
            else
            {
                button2status = false;

                //Hier Prozedur stoppen
                picoclass.whileloop = false;
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


        private void Thread_program()
        {
            short channelA = ON;   
            short channelB = OFF;

            bool progress;

            progress = picoclass.InitPS2000A();        
            progress = picoclass.SetChannel(channelA,channelB, PS2000ACSConsole.Imports.Range.Range_2V);

            progress = picoclass.SetDataBuffer();
            progress = picoclass.RunStreaming();
        }
    }
}
