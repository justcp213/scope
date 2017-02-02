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
        short[] buffer;
        int _sampleCount = 0;
        uint _startIndex = 0;
        bool _autoStop;
        bool _ready;
        short _trig;
        uint _trigAt;

        short[][] appBuffers;
        short[][] buffers;
        short[][] appDigiBuffers;
        short[][] digiBuffers;
        //Device-relevant data
        short handle;
        StringBuilder StB = new StringBuilder();

        public bool InitPS2000A()
        {

           pStat =  PS2000ACSConsole.Imports.OpenUnit(out handle, null);
            if (pStat == 0)
                return true;
            else
                return false;
            
        }

        public bool SetChannel(short channelA_OnOff, short channelB_OnOff, Imports.Range VoltageLevel)
        {
            pStat = PS2000ACSConsole.Imports.SetTriggerChannelProperties(handle, null, 0, 0, 0); //Disable Trigger

            //Channel A
            pStat = PS2000ACSConsole.Imports.SetChannel(handle,
                                                        Imports.Channel.ChannelA,
                                                        channelA_OnOff,
                                                        1,
                                                        VoltageLevel, 0
                                                        );
            //Channel B
            pStat = PS2000ACSConsole.Imports.SetChannel(handle,
                                                        Imports.Channel.ChannelB,
                                                        channelB_OnOff,
                                                        1,
                                                        VoltageLevel, 0
                                                        );

            //Disable Digitalports
            Imports.Channel port;
            short status;

            // Disable Digital ports 
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////
            for (port = Imports.Channel.PS2000A_DIGITAL_PORT0; port < Imports.Channel.PS2000A_DIGITAL_PORT1; port++) //
            {                                                                                                        //
                status = Imports.SetDigitalPort(handle, port, 0, 0);                                                 //
            }                                                                                                        //
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////


            if (pStat == 0)
                return true;
            else
                return false;
        }



        public bool SetDataBuffer()
        {
            
            buffer = new short[bufferlength];

            pStat = PS2000ACSConsole.Imports.SetDataBuffer(handle, PS2000ACSConsole.Imports.Channel.ChannelA, buffer,
                (int)bufferlength, 0, PS2000ACSConsole.Imports.RatioMode.Aggregate);


            if (pStat == 0)
                return true;
            else
                return false;
        }

        public bool RunStreaming()
        {
            uint sampleinterval = 1;
            uint preTrigger = 0;
            uint postTrigger = 10;
            uint downsampleRatio = 1;

            pStat = PS2000ACSConsole.Imports.RunStreaming(handle,
                                                          ref sampleinterval,
                                                          PS2000ACSConsole.Imports.ReportedTimeUnits.NanoSeconds,
                                                          preTrigger,
                                                          postTrigger,
                                                          false,
                                                          downsampleRatio,
                                                          PS2000ACSConsole.Imports.RatioMode.Aggregate,
                                                          bufferlength);


            if (pStat == 0)
                return true;
            else
                return false;

        }


        void StreamingCallback(short handle, int noOfSamples,uint startIndex,short ov, uint triggerAt,short triggered,short autoStop, IntPtr pVoid)
        {
            // used for streaming
            _sampleCount = noOfSamples;
            _startIndex = startIndex;
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

                                Array.Copy(buffers[ch], _startIndex, appBuffers[ch], _startIndex, _sampleCount); //max
                                Array.Copy(buffers[ch + 1], _startIndex, appBuffers[ch + 1], _startIndex, _sampleCount); //min
                            }
                        
                        break;


                }
            }
        }


        public void Loop()
        {
            System.IO.TextWriter writer = new System.IO.StreamWriter(destinationfile, false);
            writer.WriteLine("Date " + DateTime.Now.ToString("hh:mm:ss")+"\n");
            while (whileloop)
            {
                //GetStreamingLatestValues
                pStat = PS2000ACSConsole.Imports.GetStreamingLatestValues(handle,
                                                                          StreamingCallback, 
                                                                          (IntPtr)PS2000ACSConsole.Imports.Mode.ANALOGUE);
            }


        }



        public void StreamDataHandler()
        {
            uint tempBufferSize = 50000; /*  Ensure buffer is large enough */

            uint totalSamples = 0;
            uint triggeredAt = 0;
            short status;

            uint downsampleRatio;
            Imports.ReportedTimeUnits timeUnits;
            uint sampleInterval;
            Imports.RatioMode ratioMode;
            uint postTrigger;
            bool autoStop;

            int _channelCount = 0;
            int _digitalPorts = 0;


            // Use Pinned Arrays for the application buffers
            PinnedArray<short>[] appBuffersPinned = new PinnedArray<short>[_channelCount * 2];
            PinnedArray<short>[] appDigiBuffersPinned = new PinnedArray<short>[_digitalPorts * 2];

            //Größen Initialisierung der Buffers
            appBuffers = new short[_channelCount * 2][];
            buffers = new short[_channelCount * 2][];

            for (int channel = 0; channel < _channelCount * 2; channel += 2) // create data buffers
            {
                appBuffers[channel] = new short[tempBufferSize];
                appBuffers[channel + 1] = new short[tempBufferSize];

                appBuffersPinned[channel] = new PinnedArray<short>(appBuffers[channel]);
                appBuffersPinned[channel + 1] = new PinnedArray<short>(appBuffers[channel + 1]);

                buffers[channel] = new short[tempBufferSize];
                buffers[channel + 1] = new short[tempBufferSize];

                status = Imports.SetDataBuffers(handle, (Imports.Channel)(channel / 2), buffers[channel], buffers[channel + 1], (int)tempBufferSize, 0, Imports.RatioMode.Aggregate);


                downsampleRatio = 1000;
                timeUnits = Imports.ReportedTimeUnits.MicroSeconds;
                sampleInterval = 1;
                ratioMode = Imports.RatioMode.Aggregate;
                postTrigger = 1000000;
                autoStop = true;

            }
            }

        public double GetTimeInterval()
        {
            uint Timebase = 0;
            uint nosamples = 1024;
            Timebase = 8;// 6.64;
            int timeinterval;
            short oversample = 1;
            int maxsamples;

            pStat = Imports.GetTimebase(handle, Timebase, (int)nosamples, out timeinterval, oversample, out maxsamples, 0);



            if (pStat == 0)

                return Timebase;
            else
                return 0;
        }

    }
}
