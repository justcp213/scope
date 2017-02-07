using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PS2000ACSConsole;
namespace PicoGUI
{
    

    public class Worker
    {

         
        PS2000ACSConsole.Imports ImportPS = new PS2000ACSConsole.Imports();
        public bool whileloop;

        short pStat = 0;
        uint bufferlength = 50000;
        public string destinationfile = "";
        short[] buffer = new short[50000];
        int _sampleCount = 0;
        uint _startIndex = 0;
        bool _autoStop;
        bool _ready;
        short _trig;
        uint _trigAt;

        public Imports.RatioMode RatioMode = Imports.RatioMode.None;
        public Imports.Range VRange;// = Imports.Range.Range_2V;
        public Imports.Mode mode= Imports.Mode.ANALOGUE;
        public short coupling_type;

        public uint official_abtastinterval;
        public uint official_samplespersecond;
        bool ResetState = false;
        short[][] appBuffers;
        short[][] buffers;
        short[][] appDigiBuffers;
        short[][] digiBuffers;

        PinnedArray<short>[] appBuffersPinned = new PinnedArray<short>[2];
        Form1 UseForm;
        
        //Device-relevant data
        short handle;
        StringBuilder StB = new StringBuilder();

        public Worker(Form1 MainForm)
        {
            this.UseForm = MainForm;
        }



        public bool InitPS2000A()
        {
            appBuffers = new short[50000][];
            buffers = new short[50000][];
            pStat =  PS2000ACSConsole.Imports.OpenUnit(out handle, null);
            if (pStat == 0)
                return true;
            else
                return false;
            
        }

        public bool SetChannel(short channelA_OnOff, short channelB_OnOff, Imports.Range VoltageLevel)
        {

            short a = PS2000ACSConsole.Imports.SetTriggerChannelProperties(handle, null, 0, 0, 0); //Disable Trigger

            //Channel A
            short b = PS2000ACSConsole.Imports.SetChannel(handle,
                                                        Imports.Channel.ChannelA,
                                                        channelA_OnOff,
                                                        coupling_type,
                                                        VoltageLevel, 0
                                                        );
            //Channel B
            short c = PS2000ACSConsole.Imports.SetChannel(handle,
                                                        Imports.Channel.ChannelB,
                                                        channelB_OnOff,
                                                        coupling_type,
                                                        VoltageLevel, 0
                                                        );

            //Disable Digitalports
            Imports.Channel port;
            short status = 0;

            // Disable Digital ports 
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////
            for (port = Imports.Channel.PS2000A_DIGITAL_PORT0; port < Imports.Channel.PS2000A_DIGITAL_PORT1; port++) //
            {                                                                                                        //
                status = Imports.SetDigitalPort(handle, port, 0, 0);                                                 //
            }                                                                                                        //
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////


            if ((a==0) && (b==0) && (c==0) && (status ==0))
                return true;
            else
                return false;
        }



        public bool SetDataBuffer()
        {

            //buffer = new short[bufferlength];

            //pStat = PS2000ACSConsole.Imports.SetDataBuffer(handle, PS2000ACSConsole.Imports.Channel.ChannelA, buffer,
            //    (int)bufferlength, 0, RatioMode);
            buffers = new short[2][];
            buffers[0] = new short[50000];
            buffers[1] = new short[50000];


            appBuffers[0] = new short[50000];
            appBuffers[1] = new short[50000];

            appBuffersPinned[0] = new PinnedArray<short>(appBuffers[0]);

            pStat = Imports.SetDataBuffers(handle, Imports.Channel.ChannelA, buffers[0], buffers[1], (int)bufferlength, 0, RatioMode);
           // pStat = Imports.SetDataBuffer(handle, Imports.Channel.ChannelA, buffer, (int)bufferlength, 0, Imports.RatioMode.None);

            if (pStat == 0)
                return true;
            else
                return false;
        }

        public bool RunStreaming()
        {
            uint sampleinterval = 100000;//1000000;
            uint preTrigger = 0;
            uint postTrigger = 100000;//10;
            uint downsampleRatio = 1;//10;//1;

            pStat = PS2000ACSConsole.Imports.RunStreaming(handle,
                                                          ref sampleinterval,
                                                          PS2000ACSConsole.Imports.ReportedTimeUnits.NanoSeconds,
                                                          preTrigger,
                                                          postTrigger,
                                                          false,
                                                          downsampleRatio,
                                                          RatioMode,
                                                          bufferlength);


            if (pStat == 0)
                return true;
            else {
                Console.WriteLine("  - Error 0x{0:X}", pStat);
                return false;
            }
        }


        void StreamingCallback(short handle, int noOfSamples,uint startIndex,short ov, uint triggerAt,short triggered,short autoStop, IntPtr pVoid)
        {
            // used for streaming
            _sampleCount = noOfSamples;
            _startIndex = startIndex;
           // Console.WriteLine("Startindex: " + startIndex);
            _autoStop = autoStop != 0;

            // flag to say done reading data
            _ready = true;

            // flags to show if & where a trigger has occurred
            _trig = triggered;
            _trigAt = triggerAt;

            if (_sampleCount != 0)
            {
                switch ((PS2000ACSConsole.Imports.Mode)pVoid)
                {
                    case PS2000ACSConsole.Imports.Mode.ANALOGUE:

                        for (int ch = 0; ch < 1; ch ++)
                        {
                            Console.WriteLine(_startIndex);
                                Array.Copy(buffers[ch], _startIndex, appBuffers[ch], _startIndex, _sampleCount); 
                               // Array.Copy(buffers[ch + 1], _startIndex, appBuffers[ch + 1], _startIndex, _sampleCount); 
                            }
                        
                        break;


                }
            }
        }


        public void Loop()
        {
            
            System.IO.TextWriter writer = new System.IO.StreamWriter("mystream.txt", false);
            writer.WriteLine("Date " + DateTime.Now.ToString("dd/MM/yy - hh:mm:ss")+ "Timeinterval (ns)" + official_abtastinterval +"\n");
            while (whileloop)
            {
                //GetStreamingLatestValues
                pStat = PS2000ACSConsole.Imports.GetStreamingLatestValues(handle,
                                                                          StreamingCallback, 
                                                                          (System.IntPtr)mode);
                //Console.WriteLine("Stream: " + pStat);
                if (_sampleCount > 0)
                {
                    for(uint i = _startIndex; i < (_startIndex + _sampleCount); i++) {
                    //    Console.WriteLine("_startIndex " + this._startIndex + " SampleCount " + (_startIndex +_sampleCount));
                    writer.WriteLine( Convert_Dig_To_Voltage( appBuffersPinned[0].Target[i]));
                    }

                }
                if(_sampleCount >= 50000 && ResetState)
                {
                    Array.Clear(appBuffers, 0, appBuffers.Length);
                }
            }
       
            pStat = Imports.Stop(handle);
            writer.Close();
            Console.WriteLine("Streaming stopped.");
        }



      

        public double GetTimeInterval()
        {
            uint Timebase = 15;//10;    //15 entspricht 104 ns -> 1 MS bzw 9,615 MS/s, 134217728 entspricht 1s
            uint nosamples = 1024;
            
            int abtastinterval;
            short oversample = 1;
            int maxsamples;

            pStat = Imports.GetTimebase(handle, Timebase, (int)nosamples, out abtastinterval, oversample, out maxsamples, 0);

            official_abtastinterval = (uint)abtastinterval;

            SetSamplingInformation((uint)abtastinterval);
            Console.WriteLine("Abtastintervall: " + abtastinterval);
            Console.WriteLine("Maxsamples  : " + maxsamples);

            if (pStat == 0)
                
                return abtastinterval;
            else
                return 0;
        }


        private int Convert_Dig_To_Voltage(int value)
        {
            //gibt den Wert als mV zurueck

            return (value * 2000) / Imports.MaxValue;


        }

        private double Convert_Samplinginterval_To_Samplingrate(uint interval)
        {

            return (1 * Math.Pow(10, 9) / interval)  ;  // * 10^9 weil interval immer in Nanosekunden

        }

        private void SetSamplingInformation(uint abtastinterval)
        {
            //Abtastintervall hat index 0
            UseForm.SetLabels(abtastinterval.ToString() + " ns", 0);

            UseForm.SetLabels((Convert_Samplinginterval_To_Samplingrate(abtastinterval)/Math.Pow(10,6)).ToString("N"+3) + " MS/s", 1);
        }

    }
}
