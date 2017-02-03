﻿/**************************************************************************
 *
 *     ooooooooo.    o8o                       
 *     `888   `Y88.  `''                       
 *      888   .d88' oooo   .ooooo.   .ooooo.   
 *      888ooo88P'  `888  d88' `'Y8 d88' `88b  
 *      888          888  888       888   888  
 *      888          888  888   .o8 888   888  
 *     o888o        o888o `Y8bod8P' `Y8bod8P'  
 *
 *
 *    Copyright Pico Technology Ltd 1995-2015
 *
 * 
 *    For sales and general information visit
 *    https://www.picotech.com   https://www.picoauto.com
 *    
 *    For help and support visit
 *    https://www.picotech.com/tech-support
 * 
 *    If you have what it takes to join us visit
 *    http://pico.jobs/
 *
 *
 * Description:
 *   This is a console-mode program that demonstrates how to use the
 *   PS2000A driver using .NET
 *
 * Examples:
 *    Collect a block of samples immediately
 *    Collect a block of samples when a trigger event occurs
 *    Collect a stream of data immediately
 *    Collect a stream of data when a trigger event occurs
 *    
 **************************************************************************/

using System;
using System.IO;
using System.Threading;

namespace PS2000ACSConsole
{
    struct ChannelSettings
    {
        public bool DCcoupled;
        public Imports.Range range;
        public bool enabled;
    }

    class Pwq
    {
        public Imports.PwqConditions[] conditions;
        public short nConditions;
        public Imports.ThresholdDirection direction;
        public uint lower;
        public uint upper;
        public Imports.PulseWidthType type;

        public Pwq(Imports.PwqConditions[] conditions,
            short nConditions,
            Imports.ThresholdDirection direction,
            uint lower, uint upper,
            Imports.PulseWidthType type)
        {
            this.conditions = conditions;
            this.nConditions = nConditions;
            this.direction = direction;
            this.lower = lower;
            this.upper = upper;
            this.type = type;
        }
    }

    class PS2000ACSConsole
    {

        private readonly short _handle;
        public const int BUFFER_SIZE = 1024;
        public const int MAX_CHANNELS = 4;
        public const int QUAD_SCOPE = 4;
        public const int DUAL_SCOPE = 2;

        uint _timebase = 8;
        short _oversample = 1;
        bool _scaleVoltages = true;

        ushort[] inputRanges = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
        bool _ready = false;
        short _trig = 0;
        uint _trigAt = 0;
        int _sampleCount = 0;
        uint _startIndex = 0;
        bool _autoStop;

        short[][] appBuffers;
        short[][] buffers;
        short[][] appDigiBuffers;
        short[][] digiBuffers;

        private ChannelSettings[] _channelSettings;
        private int _channelCount;
        private Imports.Range _firstRange;
        private Imports.Range _lastRange;
        private int _digitalPorts;
        private Imports.ps2000aBlockReady _callbackDelegate;
        private string StreamFile = "stream.txt";
        private string BlockFile = "block.txt";

        /****************************************************************************
         * StreamingCallback
         * used by data streaming collection calls, on receipt of data.
         * used to set global flags etc checked by user routines
         ****************************************************************************/
        void StreamingCallback(short handle,
                                int noOfSamples,
                                uint startIndex,
                                short ov,
                                uint triggerAt,
                                short triggered,
                                short autoStop,
                                IntPtr pVoid)
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
                switch ((Imports.Mode)pVoid)
                {
                    case Imports.Mode.ANALOGUE:

                        for (int ch = 0; ch < _channelCount * 2; ch += 2)
                        {
                            if (_channelSettings[(int)(Imports.Channel.ChannelA + (ch / 2))].enabled)
                            {
                                Array.Copy(buffers[ch], _startIndex, appBuffers[ch], _startIndex, _sampleCount); //max
                                Array.Copy(buffers[ch + 1], _startIndex, appBuffers[ch + 1], _startIndex, _sampleCount); //min
                            }
                        }
                        break;

                    case Imports.Mode.DIGITAL:

                        for (int port = 0; port < _digitalPorts; port++) // create data buffers
                        {
                            Array.Copy(digiBuffers[port], _startIndex, appDigiBuffers[port], _startIndex, _sampleCount); //no max or min
                        }
                        break;

                    case Imports.Mode.AGGREGATED:

                        for (int port = 0; port < _digitalPorts * 2; port += 2) // create data buffers
                        {
                            Array.Copy(digiBuffers[port], _startIndex, appDigiBuffers[port], _startIndex, _sampleCount); //max
                            Array.Copy(digiBuffers[port + 1], _startIndex, appDigiBuffers[port + 1], _startIndex, _sampleCount); //min
                        }
                        break;

                }
            }
        }

        /****************************************************************************
         * BlockCallback
         * used by data block collection calls, on receipt of data.
         * used to set global flags etc checked by user routines
         ****************************************************************************/
        void BlockCallback(short handle, short status, IntPtr pVoid)
        {
            // flag to say done reading data
            _ready = true;
        }

        /****************************************************************************
         * SetDefaults - restore default settings
         ****************************************************************************/
        void SetDefaults()
        {
            for (int i = 0; i < _channelCount; i++) // reset channels to most recent settings
            {
                Imports.SetChannel(_handle, Imports.Channel.ChannelA + i,
                                   (short)(_channelSettings[(int)(Imports.Channel.ChannelA + i)].enabled ? 1 : 0),
                                   (short)(_channelSettings[(int)(Imports.Channel.ChannelA + i)].DCcoupled ? 1 : 0),
                                   _channelSettings[(int)(Imports.Channel.ChannelA + i)].range,
                                   0);
            }
        }

        /****************************************************************************
         * SetDigitals - enable Digital Channels
         ****************************************************************************/
        void SetDigitals()
        {
            Imports.Channel port;
            short status;
            short logicLevel;
            float logicVoltage = 1.5f;
            short maxLogicVoltage = 5;
            short enabled = 1;

            // Set logic threshold
            logicLevel = (short)((logicVoltage / maxLogicVoltage) * Imports.MaxLogicLevel);

            // Enable Digital ports
            for (port = Imports.Channel.PS2000A_DIGITAL_PORT0; port < Imports.Channel.PS2000A_DIGITAL_PORT2; port++)
            {
                status = Imports.SetDigitalPort(_handle, port, enabled, logicLevel);
            }

        }


        /****************************************************************************
         * DisableDigital - disable Digital Channels
         ****************************************************************************/
        void DisableDigital()
        {
            Imports.Channel port;
            short status;

            // Disable Digital ports 
            for (port = Imports.Channel.PS2000A_DIGITAL_PORT0; port < Imports.Channel.PS2000A_DIGITAL_PORT1; port++)
            {
                status = Imports.SetDigitalPort(_handle, port, 0, 0);
            }
        }


        /****************************************************************************
        * DisableAnalogue - disable analogue Channels
        ****************************************************************************/
        void DisableAnalogue()
        {
            short status;

            // Disable analogue ports
            for (int i = 0; i < _channelCount; i++) 
            {
                status = Imports.SetChannel(_handle, Imports.Channel.ChannelA + i, 0, 0, 0, 0);
                                  
            }
        }


        /****************************************************************************
         * adc_to_mv
         *
         * Convert an 16-bit ADC count into millivolts
         ****************************************************************************/
        int adc_to_mv(int raw, int ch)
        {
            return (raw * inputRanges[ch]) / Imports.MaxValue;
        }

        /****************************************************************************
         * mv_to_adc
         *
         * Convert a millivolt value into a 16-bit ADC count
         *
         *  (useful for setting trigger thresholds)
         ****************************************************************************/
        short mv_to_adc(short mv, short ch)
        {
            return (short)((mv * Imports.MaxValue) / inputRanges[ch]);
        }

        /****************************************************************************
        * ClearDataBuffers
        *
        * Stops GetData writing values to memory that has been released
        ****************************************************************************/
        void ClearDataBuffers()
        {
            int i;
            short status;

            for (i = 0; i < _channelCount; i++)
            {
                if ((status = Imports.SetDataBuffers(_handle, (Imports.Channel)i, null, null, 0, 0, Imports.RatioMode.None)) != 0)
                    Console.WriteLine("BlockDataHandler:ps2000aSetDataBuffer Channel {0} Status = 0x{1:X6}", (char)('A' + i), status);
            }

            for (i = 0; i < _digitalPorts; i++)
            {
                if ((status = Imports.SetDataBuffers(_handle, i + Imports.Channel.PS2000A_DIGITAL_PORT0, null, null, 0, 0, Imports.RatioMode.None)) != 0)
                    Console.WriteLine("BlockDataHandler:ps2000aSetDataBuffer {0} Status = 0x{1,0:X6}", i + Imports.Channel.PS2000A_DIGITAL_PORT0, status);
            }
        }

        /****************************************************************************
         * BlockDataHandler
         * - Used by all block data routines
         * - acquires data (user sets trigger mode before calling), displays 10 items
         *   and saves all to block.txt
         * Input :
         * - text : the text to display before the display of data slice
         * - offset : the offset into the data buffer to start the display's slice.
         ****************************************************************************/
        void BlockDataHandler(string text, int offset, Imports.Mode mode)
        {
            uint sampleCount = BUFFER_SIZE;
            PinnedArray<short>[] minPinned = new PinnedArray<short>[_channelCount];
            PinnedArray<short>[] maxPinned = new PinnedArray<short>[_channelCount];

            PinnedArray<short>[] digiPinned = new PinnedArray<short>[_digitalPorts];

            int timeIndisposed;
           

            if (mode == Imports.Mode.ANALOGUE || mode == Imports.Mode.MIXED)
            {
                for (int i = 0; i < _channelCount; i++)
                {
                    short[] minBuffers = new short[sampleCount];
                    short[] maxBuffers = new short[sampleCount];
                    minPinned[i] = new PinnedArray<short>(minBuffers);
                    maxPinned[i] = new PinnedArray<short>(maxBuffers);
                    int status = Imports.SetDataBuffers(_handle, (Imports.Channel)i, maxBuffers, minBuffers, (int)sampleCount, 0, Imports.RatioMode.None);
                    Console.WriteLine("BlockDataHandler:ps2000aSetDataBuffer Channel {0} Status = 0x{1:X6}", (char)('A' + i), status);
                }
            }


            if (mode == Imports.Mode.DIGITAL || mode == Imports.Mode.MIXED)
            {
                for (int i = 0; i < _digitalPorts; i++)
                {
                    short[] digiBuffer = new short[sampleCount];
                    digiPinned[i] = new PinnedArray<short>(digiBuffer);
                    int status = Imports.SetDataBuffer(_handle, i + Imports.Channel.PS2000A_DIGITAL_PORT0, digiBuffer, (int)sampleCount, 0, Imports.RatioMode.None);
                    Console.WriteLine("BlockDataHandler:ps2000aSetDataBuffer {0} Status = 0x{1,0:X6}", i + Imports.Channel.PS2000A_DIGITAL_PORT0, status);
                }
            }
           


            /* find the maximum number of samples, the time interval (in timeUnits),
             * the most suitable time units, and the maximum _oversample at the current _timebase*/
            int timeInterval;
            int maxSamples;
            while (Imports.GetTimebase(_handle, _timebase, (int)sampleCount, out timeInterval, _oversample, out maxSamples, 0) != 0)
            {
                Console.WriteLine("Selected timebase {0} could not be used\n", _timebase);
                _timebase++;

            }
            Console.WriteLine("Timebase: {0}\toversample:{1}\n", _timebase, _oversample);

            /* Start it collecting, then wait for completion*/
            _ready = false;
            _callbackDelegate = BlockCallback;
            Imports.RunBlock(_handle, 0, (int)sampleCount, _timebase, _oversample, out timeIndisposed, 0, _callbackDelegate, IntPtr.Zero);

            Console.WriteLine("Waiting for data...Press a key to abort");

            while (!_ready && !Console.KeyAvailable)
            {
                Thread.Sleep(100);
            }
            if (Console.KeyAvailable) Console.ReadKey(true); // clear the key


            if (_ready)
            {
                short overflow;
                Imports.GetValues(_handle, 0, ref sampleCount, 1, Imports.DownSamplingMode.None, 0, out overflow);

                /* Print out the first 10 readings, converting the readings to mV if required */
                Console.WriteLine(text);


                 if (mode == Imports.Mode.ANALOGUE || mode == Imports.Mode.MIXED)
                 {
                     Console.WriteLine("Value {0}", (_scaleVoltages) ? ("mV") : ("ADC Counts"));

                     for (int ch = 0; ch < _channelCount; ch++)
                     {
                         Console.Write("Channel{0}                 ", (char)('A' + ch));
                     }
                 }
                 if (mode == Imports.Mode.DIGITAL || mode == Imports.Mode.MIXED)
                     Console.Write("DIGITAL VALUE");

                 Console.WriteLine();


                for (int i = offset; i < offset + 10; i++)
                {
                    if (mode == Imports.Mode.ANALOGUE || mode == Imports.Mode.MIXED)
                    {
                        for (int ch = 0; ch < _channelCount; ch++)
                        {
                            if (_channelSettings[ch].enabled)
                            {
                                Console.Write("{0,8}                 ", _scaleVoltages ?
                                    adc_to_mv(maxPinned[ch].Target[i], (int)_channelSettings[(int)(Imports.Channel.ChannelA + ch)].range)  // If _scaleVoltages, show mV values
                                    : maxPinned[ch].Target[i]);                                                                           // else show ADC counts
                            }
                        }
                    }
                    if (mode == Imports.Mode.DIGITAL || mode == Imports.Mode.MIXED)
                    {
                        short digiValue = digiPinned[1].Target[i];
                        digiValue <<= 8;
                        digiValue |= digiPinned[0].Target[i];
                        Console.Write("0x{0,4:X}", digiValue.ToString("X4"));
                    }
                    Console.WriteLine();
                }

                if (mode == Imports.Mode.ANALOGUE || mode == Imports.Mode.MIXED)
                {
                    sampleCount = Math.Min(sampleCount, BUFFER_SIZE);
                    TextWriter writer = new StreamWriter(BlockFile, false);
                    writer.Write("For each of the {0} Channels, results shown are....", _channelCount);
                    writer.WriteLine();
                    writer.WriteLine("Time interval Maximum Aggregated value ADC Count & mV, Minimum Aggregated value ADC Count & mV");
                    writer.WriteLine();

                    for (int i = 0; i < _channelCount; i++)
                        writer.Write("Time  Ch  Max ADC    Max mV   Min ADC    Min mV   ");
                    writer.WriteLine();

                    for (int i = 0; i < sampleCount; i++)
                    {
                        for (int ch = 0; ch < _channelCount; ch++)
                        {
                            writer.Write("{0,5}  ", (i * timeInterval));
                            if (_channelSettings[ch].enabled)
                            {
                                writer.Write("Ch{0} {1,7}   {2,7}   {3,7}   {4,7}   ",
                                               (char)('A' + ch),
                                               maxPinned[ch].Target[i],
                                               adc_to_mv(maxPinned[ch].Target[i],
                                                         (int)_channelSettings[(int)(Imports.Channel.ChannelA + ch)].range),
                                               minPinned[ch].Target[i],
                                               adc_to_mv(minPinned[ch].Target[i],
                                                         (int)_channelSettings[(int)(Imports.Channel.ChannelA + ch)].range));
                            }
                        }
                        writer.WriteLine();
                    }
                    writer.Close();
                }
            }
            else
            {
                Console.WriteLine("data collection aborted");
            }

            Imports.Stop(_handle);

            if (mode == Imports.Mode.ANALOGUE || mode == Imports.Mode.MIXED)
            {
                foreach (PinnedArray<short> p in minPinned)
                {
                    if (p != null)
                        p.Dispose();
                }
                foreach (PinnedArray<short> p in maxPinned)
                {
                    if (p != null)
                        p.Dispose();
                }
            }

            if (mode == Imports.Mode.DIGITAL || mode == Imports.Mode.MIXED)
            {
                foreach (PinnedArray<short> p in digiPinned)
                {
                    if (p != null)
                        p.Dispose();
                }
            }
        }


        /****************************************************************************
         * RapidBlockDataHandler
         * - Used by all the CollectBlockRapid routine
         * - acquires data (user sets trigger mode before calling), displays 10 items
         *   and saves all to data.txt
         * Input :
         * - nRapidCaptures : the user specified number of blocks to capture
         ****************************************************************************/
        private void RapidBlockDataHandler(ushort nRapidCaptures)
        {
            short status;
            int numChannels = _channelCount;
            uint numSamples = BUFFER_SIZE;

            // Run the rapid block capture
            int timeIndisposed;
            _ready = false;

            _timebase = 160;
            _oversample = 1;

            _callbackDelegate = BlockCallback;
            status = Imports.RunBlock(_handle,
                        0,
                        (int)numSamples,
                        _timebase,
                        _oversample,
                        out timeIndisposed,
                        0,
                        _callbackDelegate,
                        IntPtr.Zero);

            if (status != 0)
            {
                Console.WriteLine("RunBlock status error 0x{0:X}", status);
                return;
            }


            Console.WriteLine("Waiting for data...Press a key to abort");

            while (!_ready && !Console.KeyAvailable)
            {
                Thread.Sleep(100);
            }
            if (Console.KeyAvailable) Console.ReadKey(true); // clear the key

            Imports.Stop(_handle);


            // Set up the data arrays and pin them
            short[][][] values = new short[nRapidCaptures][][];
            PinnedArray<short>[,] pinned = new PinnedArray<short>[nRapidCaptures, numChannels];

            for (ushort segment = 0; segment < nRapidCaptures; segment++)
            {
                values[segment] = new short[numChannels][];
                for (short channel = 0; channel < numChannels; channel++)
                {
                    if (_channelSettings[channel].enabled)
                    {
                        values[segment][channel] = new short[numSamples];
                        pinned[segment, channel] = new PinnedArray<short>(values[segment][channel]);

                        status = Imports.SetDataBuffer(_handle,
                                               (Imports.Channel)channel,
                                               values[segment][channel],
                                               (int)numSamples,
                                               segment,
                                               0);
                    }
                    else
                    {
                        status = Imports.SetDataBuffer(_handle,
                                   (Imports.Channel)channel,
                                    null,
                                    0,
                                    segment,
                                    0);

                    }
                }
            }

            // Read the data
            short[] overflows = new short[nRapidCaptures];

            status = Imports.GetValuesRapid(_handle, ref numSamples, 0, (ushort)(nRapidCaptures - 1), 1, Imports.DownSamplingMode.None, overflows);
            if (status != 0)
            {
                Console.WriteLine("GetValuesRapid status error 0x{0:X}", status);
                return;
            }
           

            /* Print out the first 10 readings, converting the readings to mV if required */
            Console.WriteLine("\nValues in {0}", (_scaleVoltages) ? ("mV") : ("ADC Counts"));

            for (int seg = 0; seg < nRapidCaptures; seg++)
            {
                Console.WriteLine("Capture {0}", seg);
                for (int i = 0; i < 10; i++)
                {
                    for (int chan = 0; chan < _channelCount; chan++)
                    {
                        Console.Write("{0}\t", _scaleVoltages ? 
                                                adc_to_mv(pinned[seg, chan].Target[i], (int)_channelSettings[(int)(Imports.Channel.ChannelA + chan)].range) // If _scaleVoltages, show mV values
                                                : pinned[seg, chan].Target[i]);                                                                             // else show ADC counts
                    }
                    Console.WriteLine();
                }
                Console.WriteLine();
            }

            // Un-pin the arrays
            foreach (PinnedArray<short> p in pinned)
            {
                if (p != null)
                    p.Dispose();
            }

            //TODO: Do what ever is required with the data here.
        }


        /****************************************************************************
        *  WaitForKey
        *  Wait for user's keypress
        ****************************************************************************/
        private static void WaitForKey()
        {
            while (!Console.KeyAvailable) Thread.Sleep(100);
            if (Console.KeyAvailable) Console.ReadKey(true); // clear the key
        }

        /****************************************************************************
        *  SetTrigger
        *  this function sets all the required trigger parameters, and calls the 
        *  triggering functions
        ****************************************************************************/
        short SetTrigger(Imports.TriggerChannelProperties[] channelProperties, 
                        short nChannelProperties, 
                        Imports.TriggerConditions[] triggerConditions, 
                        short nTriggerConditions, 
                        Imports.ThresholdDirection[] directions, 
                        Pwq pwq, 
                        uint delay, 
                        short auxOutputEnabled, 
                        int autoTriggerMs,
                        Imports.DigitalChannelDirections[] digitalDirections,
                        short nDigitalDirections)
        {
            short status;

            if ((status = Imports.SetTriggerChannelProperties(_handle, channelProperties, nChannelProperties, auxOutputEnabled,autoTriggerMs)) != 0)
            {
                return status;
            }

            if ((status = Imports.SetTriggerChannelConditions(_handle, triggerConditions, nTriggerConditions)) != 0)
            {
                return status;
            }

            if (directions == null) directions = new Imports.ThresholdDirection[] { Imports.ThresholdDirection.None, 
                Imports.ThresholdDirection.None, Imports.ThresholdDirection.None, Imports.ThresholdDirection.None, 
                Imports.ThresholdDirection.None, Imports.ThresholdDirection.None};

            if ((status = Imports.SetTriggerChannelDirections(_handle,
                                                              directions[(int)Imports.Channel.ChannelA],
                                                              directions[(int)Imports.Channel.ChannelB],
                                                              directions[(int)Imports.Channel.ChannelC],
                                                              directions[(int)Imports.Channel.ChannelD],
                                                              directions[(int)Imports.Channel.External],
                                                              directions[(int)Imports.Channel.Aux])) != 0)
            {
                return status;
            }

            if ((status = Imports.SetTriggerDelay(_handle, delay)) != 0)
            {
                return status;
            }

            if (pwq == null) pwq = new Pwq(null, 0, Imports.ThresholdDirection.None, 0, 0, Imports.PulseWidthType.None);

            status = Imports.SetPulseWidthQualifier(_handle, pwq.conditions,
                                                    pwq.nConditions, pwq.direction,
                                                    pwq.lower, pwq.upper, pwq.type);

            if (_digitalPorts > 0)
            {
                if ((status = Imports.SetTriggerDigitalPort(_handle, digitalDirections, nDigitalDirections)) !=0 )
                {
                    return status;
                }
            }

            return status;
        }

        /****************************************************************************
        * CollectBlockImmediate
        *  this function demonstrates how to collect a single block of data
        *  from the unit (start collecting immediately)
        ****************************************************************************/
        void CollectBlockImmediate()
        {
            Console.WriteLine("Collect Block Immediate");
            Console.WriteLine("Data is written to disk file ({0})", BlockFile);
            Console.WriteLine("Press a key to start...");
            Console.WriteLine();
            WaitForKey();

            SetDefaults();

            /* Trigger disabled	*/
            SetTrigger(null, 0, null, 0, null, null, 0, 0, 0, null, 0);

            BlockDataHandler("First 10 readings", 0, Imports.Mode.ANALOGUE);
        }

        /****************************************************************************
        *  CollectBlockRapid
        *  this function demonstrates how to collect blocks of data
        *  using the RapidCapture function
        ****************************************************************************/
        void CollectBlockRapid()
        {
            ushort numRapidCaptures = 1;
            short status;

            Console.WriteLine("Collect Rapid Block");

            numRapidCaptures = 10;

            status = Imports.SetNoOfRapidCaptures(_handle, numRapidCaptures);
            if (status != 0)
                Console.WriteLine("SetNoOfRapidCaptures status error 0x{0:X}", status);

            int maxSamples;
            status = Imports.MemorySegments(_handle, numRapidCaptures, out maxSamples);

            if (status != 0)
                Console.WriteLine("MemorySegments status error 0x{0:X}", status);

            Console.WriteLine("Collecting {0} rapid blocks. Press a key to start...", numRapidCaptures);

            WaitForKey();

            SetDefaults();

            /* Trigger is optional, disable it for now	*/
            SetTrigger(null, 0, null, 0, null, null, 0, 0, 0, null, 0);

            RapidBlockDataHandler(numRapidCaptures);
        }

        /****************************************************************************
       *  CollectBlockTriggered
       *  this function demonstrates how to collect a single block of data from the
       *  unit, when a trigger event occurs.
       ****************************************************************************/
        void CollectBlockTriggered()
        {
            short triggerVoltage = mv_to_adc(1000, (short)_channelSettings[(int)Imports.Channel.ChannelA].range); // ChannelInfo stores ADC counts
            Imports.TriggerChannelProperties[] sourceDetails = new Imports.TriggerChannelProperties[] {
                new Imports.TriggerChannelProperties(triggerVoltage,
                                             256*10,
                                             triggerVoltage,
                                             256*10,
                                             Imports.Channel.ChannelA,
                                             Imports.ThresholdMode.Level)};

          
            Imports.TriggerConditions[] conditions = new Imports.TriggerConditions[] {
              new Imports.TriggerConditions(Imports.TriggerState.True,                      // Channel A
                                            Imports.TriggerState.DontCare,                  // Channel B
                                            Imports.TriggerState.DontCare,                  // Channel C
                                            Imports.TriggerState.DontCare,                  // Channel D
                                            Imports.TriggerState.DontCare,                  // external
                                            Imports.TriggerState.DontCare,                  // aux
                                            Imports.TriggerState.DontCare,                  // pwq
                                            Imports.TriggerState.DontCare                   // digital
                                            )};

            Imports.ThresholdDirection[] directions = new Imports.ThresholdDirection[]
	                                        { Imports.ThresholdDirection.Rising,            // Channel A
                                            Imports.ThresholdDirection.None,                // Channel B
                                            Imports.ThresholdDirection.None,                // Channel C
                                            Imports.ThresholdDirection.None,                // Channel D
                                            Imports.ThresholdDirection.None,                // ext
                                            Imports.ThresholdDirection.None };              // aux

            Console.WriteLine("Collect Block Triggered");
            Console.WriteLine("Data is written to disk file ({0})", BlockFile);
           
            Console.Write("Collects when value rises past {0}", (_scaleVoltages) ?
                          adc_to_mv(sourceDetails[0].ThresholdMajor,
                                    (int)_channelSettings[(int)Imports.Channel.ChannelA].range)
                                    : sourceDetails[0].ThresholdMajor); 
            Console.WriteLine("{0}", (_scaleVoltages) ? ("mV") : ("ADC Counts"));

            Console.WriteLine("Press a key to start...");
            WaitForKey();

            SetDefaults();

            /* Trigger enabled
             * Rising edge
             * Threshold = 1000mV */
            SetTrigger(sourceDetails, 1, conditions, 1, directions, null, 0, 0, 0, null, 0);

            BlockDataHandler("Ten readings after trigger", 0, Imports.Mode.ANALOGUE);
        }

        /****************************************************************************
        * Initialise unit' structure with Variant specific defaults
        ****************************************************************************/
        void GetDeviceInfo()
        {
            int variant = 0;
            string[] description = {
                           "Driver Version    ",
                           "USB Version       ",
                           "Hardware Version  ",
                           "Variant Info      ",
                           "Serial            ",
                           "Cal Date          ",
                           "Kernel Ver        ",
                           "Digital Hardware  ",
                           "Analogue Hardware "
                         };

            System.Text.StringBuilder line = new System.Text.StringBuilder(80);

            if (_handle >= 0)
            {
                for (int i = 0; i < 9; i++)
                {
                    short requiredSize;
                    Imports.GetUnitInfo(_handle, line, 80, out requiredSize, i);
                    
                    if (i == 3)
                    {
                        if (line.ToString().EndsWith("MSO"))
                        {
                            variant = Convert.ToInt16((line.ToString().Remove(4, 4)));  // Use the numeric part of the variant number
                        }
                        else if (line.ToString().EndsWith("A"))
                        {
                            variant = Convert.ToInt16((line.ToString().Remove(4, 1)));  // Handle 'A' variants
                        }
                        else
                        {
                            variant = Convert.ToInt16(line.ToString());
                        }

                    }

                    Console.WriteLine("{0}: {1}", description[i], line);
                }

                switch (variant)
                {
                    case (int)Imports.Model.PS2205MSO:
                        _firstRange = Imports.Range.Range_50MV;
                        _lastRange = Imports.Range.Range_20V;
                        _channelCount = DUAL_SCOPE;
                        _digitalPorts = 2;
                        break;

                    default:
                        _firstRange = Imports.Range.Range_50MV;
                        _lastRange = Imports.Range.Range_20V;
                        _channelCount = DUAL_SCOPE;
                        _digitalPorts = 0;
                        break;

                }
            }
        }

        /****************************************************************************
         * Select input voltage ranges for channels A and B
         ****************************************************************************/
        void SetVoltages()
        {
            bool valid = false;

            /* See what ranges are available... */
            for (int i = (int)_firstRange; i <= (int)_lastRange; i++)
            {
                Console.WriteLine("{0} . {1} mV", i, inputRanges[i]);
            }

            /* Ask the user to select a range */
            Console.WriteLine("Specify voltage range ({0}..{1})", _firstRange, _lastRange);
            Console.WriteLine("99 - switches channel off");
            
            for (int ch = 0; ch < _channelCount; ch++)
            {
                Console.WriteLine("");
                uint range = 8;

                do
                {
                    try
                    {
                        Console.WriteLine("Channel: {0}", (char)('A' + ch));
                        range = uint.Parse(Console.ReadLine());
                        valid = true;
                    }
                    catch (FormatException)
                    {
                        valid = false;
                        Console.WriteLine("\nEnter numeric values only");
                    }

                } while ((range != 99 && (range < (uint)_firstRange || range > (uint)_lastRange) || !valid));


                if (range != 99)
                {
                    _channelSettings[ch].range = (Imports.Range)range;
                    Console.WriteLine(" = {0} mV", inputRanges[range]);
                    _channelSettings[ch].enabled = true;
                }
                else
                {
                    Console.WriteLine("Channel Switched off");
                    _channelSettings[ch].enabled = false;
                }
            }

            SetDefaults();  // Set defaults now, so that if all but 1 channels get switched off, timebase updates to timebase 0 will work
        }

        /****************************************************************************
         *
         * Select _timebase, set _oversample to on and time units as nano seconds
         *
         ****************************************************************************/
        void SetTimebase()
        {
            int timeInterval;
            int maxSamples;
            bool valid = false;

            Console.WriteLine("Specify timebase");

            do
            {
                try
                {
                    _timebase = uint.Parse(Console.ReadLine());
                    valid = true;
                }
                catch (FormatException)
                {
                    valid = false;
                    Console.WriteLine("\nEnter numeric values only");
                }

            } while (!valid);

            while (Imports.GetTimebase(_handle, _timebase, BUFFER_SIZE, out timeInterval, 1, out maxSamples, 0) != 0)
            {
                Console.WriteLine("Selected timebase {0} could not be used", _timebase);
                _timebase++;
            }

            Console.WriteLine("Timebase {0} - {1} ns", _timebase, timeInterval);
            _oversample = 1;
        }


        /****************************************************************************
        * Stream Data Handler
        * - Used by the two stream data examples - untriggered and triggered
        * Inputs:
        * - preTrigger - the number of samples in the pre-trigger phase 
        *					(0 if no trigger has been set)
        ***************************************************************************/
        void StreamDataHandler(uint preTrigger, Imports.Mode mode)
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

            // Use Pinned Arrays for the application buffers
            PinnedArray<short>[] appBuffersPinned = new PinnedArray<short>[_channelCount * 2];
            PinnedArray<short>[] appDigiBuffersPinned = new PinnedArray<short>[_digitalPorts * 2];

            switch (mode)
            {
                case Imports.Mode.ANALOGUE:

                    //Größen Initialisierung der Buffers
                    appBuffers = new short[_channelCount * 2][];
                    buffers = new short[_channelCount * 2][];

                    for (int channel = 0; channel < _channelCount*2; channel+=2) // create data buffers
                    {
                        appBuffers[channel] = new short[tempBufferSize];
                        appBuffers[channel + 1] = new short[tempBufferSize];

                        appBuffersPinned[channel] = new PinnedArray<short>(appBuffers[channel]);
                        appBuffersPinned[channel + 1] = new PinnedArray<short>(appBuffers[channel + 1]);

                        buffers[channel] = new short[tempBufferSize];
                        buffers[channel + 1] = new short[tempBufferSize];

                        status = Imports.SetDataBuffers(_handle, (Imports.Channel)(channel / 2), buffers[channel], buffers[channel+1], (int)tempBufferSize, 0, Imports.RatioMode.Aggregate);
                    }

                    downsampleRatio = 1000;
                    timeUnits = Imports.ReportedTimeUnits.MicroSeconds;
                    sampleInterval = 1;
                    ratioMode = Imports.RatioMode.Aggregate;
                    postTrigger = 1000000;
                    autoStop = true;
                    break;

                case Imports.Mode.AGGREGATED:

                    appDigiBuffers = new short[_digitalPorts * 2][];
                    digiBuffers = new short[_digitalPorts * 2][];

                    for (int port = 0; port < _digitalPorts * 2; port += 2) // create data buffers
                    {
                        appDigiBuffers[port] = new short[tempBufferSize];
                        appDigiBuffers[port + 1] = new short[tempBufferSize];

                        appDigiBuffersPinned[port] = new PinnedArray<short>(appDigiBuffers[port]);
                        appDigiBuffersPinned[port + 1] = new PinnedArray<short>(appDigiBuffers[port + 1]);

                        digiBuffers[port] = new short[tempBufferSize];
                        digiBuffers[port + 1] = new short[tempBufferSize];

                        status = Imports.SetDataBuffers(_handle, (Imports.Channel)(port / 2) + 0x80, digiBuffers[port], digiBuffers[port + 1], (int)tempBufferSize, 0, Imports.RatioMode.Aggregate);
                    }

                    downsampleRatio = 10;
                    timeUnits = Imports.ReportedTimeUnits.MilliSeconds;
                    sampleInterval = 10;
                    ratioMode = Imports.RatioMode.Aggregate;
                    postTrigger = 10;
                    autoStop = false;
                    break;

                case Imports.Mode.DIGITAL:

                    appDigiBuffers = new short[_digitalPorts][];
                    digiBuffers = new short[_digitalPorts][];

                    for (int port = 0; port < _digitalPorts; port++) // create data buffers
                    {
                        appDigiBuffers[port] = new short[tempBufferSize];
                        appDigiBuffersPinned[port] = new PinnedArray<short>(appDigiBuffers[port]);

                        digiBuffers[port] = new short[tempBufferSize];

                        status = Imports.SetDataBuffer(_handle, (Imports.Channel)port + 0x80, digiBuffers[port], (int)tempBufferSize, 0, Imports.RatioMode.None);
                    }

                    downsampleRatio = 1;
                    timeUnits = Imports.ReportedTimeUnits.MilliSeconds;
                    sampleInterval = 10;
                    ratioMode = Imports.RatioMode.None;
                    postTrigger = 10;
                    autoStop = false;
                    break;


                default:
                    
                    downsampleRatio = 1;
                    timeUnits = Imports.ReportedTimeUnits.MilliSeconds;
                    sampleInterval = 10;
                    ratioMode = Imports.RatioMode.None;
                    postTrigger = 10;
                    autoStop = false;
                    break;
            }


            if (autoStop)
            {
                Console.WriteLine("Streaming Data for {0} samples", postTrigger / downsampleRatio);

                if (preTrigger > 0)
                {// we pass 0 for preTrigger if we're not setting up a trigger
                    Console.WriteLine("after the trigger occurs");
                    Console.WriteLine("Note: {0} Pre Trigger samples before Trigger arms\n\n", preTrigger / downsampleRatio);
                }
                else
                {
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("\nStreaming Data continually...\n\n");
            }

            _autoStop = false;
            
            if ((status = Imports.RunStreaming(_handle, ref sampleInterval, timeUnits, preTrigger, postTrigger - preTrigger, autoStop, downsampleRatio, ratioMode, tempBufferSize)) != 0)
            {
                Console.WriteLine("StreamDataHandler:ps2000aRunStreaming Status = 0x{0:X6}", status);
                _autoStop = true;           // if there's a problem, set _autoStop = true to drop out clean up memory, and close the text writer.
            }

            Console.WriteLine("Run Streaming : {0} ", status);

            Console.WriteLine("Streaming data...Press a key to abort");

            TextWriter writer = null;

            if (mode == Imports.Mode.ANALOGUE)
            {
                writer = new StreamWriter(StreamFile, false);

                writer.Write("For each of the {0} Channels, results shown are....", _channelCount);
                writer.WriteLine();
                writer.WriteLine("Maximum Aggregated value ADC Count & mV, Minimum Aggregated value ADC Count & mV");
                writer.WriteLine();

                for (int i = 0; i < _channelCount; i++)
                {
                    writer.Write("Ch  Max ADC    Max mV   Min ADC    Min mV   ");
                }
                writer.WriteLine();
            }

            while (!_autoStop && !Console.KeyAvailable)
            {
                /* Poll until data is received. Until then, GetStreamingLatestValues wont call the callback */
                Thread.Sleep(0);
                _ready = false;

                status = Imports.GetStreamingLatestValues(_handle, StreamingCallback, (System.IntPtr)mode);
               

                if (_ready && _sampleCount > 0) /* can be ready and have no data, if autoStop has fired */
                {

                    
                    totalSamples += (uint)_sampleCount;

                    Console.Write("Collected {0,4} samples, index = {1,5} Total = {2,5}", _sampleCount, _startIndex, totalSamples);

                    if (_trig > 0)
                    {
                        Console.Write("\tTrig at Index {0}", triggeredAt);
                    }

                    Console.WriteLine();
                    
                    for (uint i = _startIndex; i < (_startIndex + _sampleCount); i++)
                    {
                        if (mode == Imports.Mode.ANALOGUE)
                        {
                            for (int ch = 0; ch < _channelCount * 2; ch += 2)
                            {
                                if (_channelSettings[ch / 2].enabled)
                                {
                                    writer.Write("Ch{0} {1,7}   {2,7}   {3,7}   {4,7}   ",
                                                            (char)('A' + (ch / 2)),
                                                            appBuffersPinned[ch].Target[i],
                                                            adc_to_mv(appBuffersPinned[ch].Target[i], (int)_channelSettings[(int)(Imports.Channel.ChannelA + (ch / 2))].range),
                                                            appBuffersPinned[ch + 1].Target[i],
                                                            adc_to_mv(appBuffersPinned[ch + 1].Target[i], (int)_channelSettings[(int)(Imports.Channel.ChannelA + (ch / 2))].range));
                                }
                            }
                            writer.WriteLine();
                        }

                    }
                }
            }

            if (Console.KeyAvailable)
            {
                Console.ReadKey(true); // clear the key
            }

            Imports.Stop(_handle);

            if (writer != null)
            { 
                writer.Close();
            }

            if (!_autoStop)
            {
                Console.WriteLine("\ndata collection aborted");
            } 
        }


        /****************************************************************************
        * CollectStreamingImmediate
        *  this function demonstrates how to collect a stream of data
        *  from the unit (start collecting immediately)
        ***************************************************************************/
        void CollectStreamingImmediate()
        {
            SetDefaults();

            Console.WriteLine("Collect Streaming Immediate");
            Console.WriteLine("Data is written to disk file ({0})", StreamFile);
            Console.WriteLine("Press a key to start...");
            WaitForKey();

            /* Trigger disabled	*/
            SetTrigger(null, 0, null, 0, null, null, 0, 0, 0, null, 0);

            StreamDataHandler(0, Imports.Mode.ANALOGUE);
        }


        /****************************************************************************
        * CollectStreamingTriggered
        *  this function demonstrates how to collect a stream of data
        *  from the unit (start collecting on trigger)
        ***************************************************************************/
        void CollectStreamingTriggered()
        {
            short triggerVoltage = mv_to_adc(1000, (short)_channelSettings[(int)Imports.Channel.ChannelA].range); // ChannelInfo stores ADC counts

            Imports.TriggerChannelProperties[] sourceDetails = new Imports.TriggerChannelProperties[] {
                new Imports.TriggerChannelProperties( triggerVoltage, 
                                                        256 * 10, 
                                                        triggerVoltage, 
                                                        256 * 10, 
                                                        Imports.Channel.ChannelA, 
                                                        Imports.ThresholdMode.Level )};

            Imports.TriggerConditions[] conditions = new Imports.TriggerConditions[] {
              new Imports.TriggerConditions(Imports.TriggerState.True,
                                            Imports.TriggerState.DontCare,
                                            Imports.TriggerState.DontCare,
                                            Imports.TriggerState.DontCare,
                                            Imports.TriggerState.DontCare,
                                            Imports.TriggerState.DontCare,
                                            Imports.TriggerState.DontCare,
                                            Imports.TriggerState.DontCare)};

            Imports.ThresholdDirection[] directions = new Imports.ThresholdDirection[]
	                                        { Imports.ThresholdDirection.Rising,
                                            Imports.ThresholdDirection.None, 
                                            Imports.ThresholdDirection.None, 
                                            Imports.ThresholdDirection.None, 
                                            Imports.ThresholdDirection.None,
                                            Imports.ThresholdDirection.None };

            Console.WriteLine("Collect Streaming Triggered");
            Console.WriteLine("Data is written to disk file ({0})", StreamFile);

            Console.Write("Indicate when value rises past {0}", (_scaleVoltages) ?
                         adc_to_mv(sourceDetails[0].ThresholdMajor,
                                   (int)_channelSettings[(int)Imports.Channel.ChannelA].range)
                                   : sourceDetails[0].ThresholdMajor);

            Console.WriteLine("{0}", (_scaleVoltages) ? ("mV") : ("ADC Counts"));
          
            Console.WriteLine("Press a key to start...");
            WaitForKey();
            SetDefaults();

            /* Trigger enabled
             * Rising edge
             * Threshold = 1000mV */

            SetTrigger(sourceDetails, 1, conditions, 1, directions, null, 0, 0, 0, null, 0);

            StreamDataHandler(100000, Imports.Mode.ANALOGUE);
        }

        /****************************************************************************
        * DisplaySettings 
        * Displays information about the user configurable settings in this example
        ***************************************************************************/
        void DisplaySettings()
        {
            int ch;
            int voltage;

            Console.WriteLine("\n\nReadings will be scaled in {0}", (_scaleVoltages) ? ("mV") : ("ADC counts"));

            for (ch = 0; ch < _channelCount; ch++)
            {
                if (!_channelSettings[ch].enabled)
                {
                    Console.WriteLine("Channel {0} Voltage Range = Off", (char)('A' + ch));
                }
                else
                {
                    voltage = inputRanges[(int)_channelSettings[ch].range];
                    Console.Write("Channel {0} Voltage Range = ", (char)('A' + ch));

                    if (voltage < 1000)
                    {
                        Console.WriteLine("{0}mV", voltage);
                    }
                    else
                    {
                        Console.WriteLine("{0}V", voltage / 1000);
                    }
                }
            }

            Console.WriteLine();
        }



        /****************************************************************************
        * DigitalBlockImmediate
        * Collect a block of data from the digital ports with triggering disabled
        ***************************************************************************/
        void DigitalBlockImmediate()
        {

            Console.WriteLine("Digital Block Immediate");
            Console.WriteLine("Press a key to start...");
            WaitForKey();

            SetTrigger(null, 0, null, 0, null, null, 0, 0, 0, null, 0);

            BlockDataHandler("First 10 readings\n", 0, Imports.Mode.DIGITAL);
        }

      
        /****************************************************************************
        * DigitalBlockTriggered
        * Collect a block of data from the digital ports with triggering disabled
        ***************************************************************************/
        void DigitalBlockTriggered()
        {
            Console.WriteLine("Digital Block Triggered");
            Console.WriteLine("Collect block of data when the trigger occurs...");
            Console.WriteLine("Digital Channel   3   --- Rising");
            Console.WriteLine("Digital Channel  13   --- High");
            Console.WriteLine("Other Digital Channels - Don't Care");

            Console.WriteLine("Press a key to start...");
            WaitForKey();


            Imports.TriggerConditions[] conditions = new Imports.TriggerConditions[] {
              new Imports.TriggerConditions(Imports.TriggerState.DontCare,                  // Channel A
                                            Imports.TriggerState.DontCare,                  // Channel B
                                            Imports.TriggerState.DontCare,                  // Channel C
                                            Imports.TriggerState.DontCare,                  // Channel D
                                            Imports.TriggerState.DontCare,                  // external
                                            Imports.TriggerState.DontCare,                  // aux
                                            Imports.TriggerState.DontCare,                  // pwq
                                            Imports.TriggerState.True                       // digital
                                            )};


            Imports.DigitalChannelDirections[] digitalDirections = new Imports.DigitalChannelDirections[2];

            digitalDirections[0].DigiPort = Imports.DigitalChannel.PS2000A_DIGITAL_CHANNEL_3;
            digitalDirections[0].DigiDirection = Imports.DigitalDirection.PS2000A_DIGITAL_DIRECTION_RISING;

            digitalDirections[1].DigiPort = Imports.DigitalChannel.PS2000A_DIGITAL_CHANNEL_13;
            digitalDirections[1].DigiDirection = Imports.DigitalDirection.PS2000A_DIGITAL_DIRECTION_HIGH;

            SetTrigger(null, 0, conditions, 1, null, null, 0, 0, 0, digitalDirections, 2);

            BlockDataHandler("First 10 readings\n", 0, Imports.Mode.DIGITAL);
        }


        /****************************************************************************
        * ANDAnalogueDigitalTriggered
        *  this function demonstrates how to combine Digital AND Analogue triggers
        *  to collect a block of data.
        ****************************************************************************/
        void ANDAnalogueDigitalTriggered()
        {
            Console.WriteLine("Analogue AND Digital Triggered Block");
        

            short triggerVoltage = mv_to_adc(1000, (short)_channelSettings[(int)Imports.Channel.ChannelA].range); // ChannelInfo stores ADC counts
            
            Imports.TriggerChannelProperties[] sourceDetails = new Imports.TriggerChannelProperties[] {
                new Imports.TriggerChannelProperties(triggerVoltage,
                                             256*10,
                                             triggerVoltage,
                                             256*10,
                                             Imports.Channel.ChannelA,
                                             Imports.ThresholdMode.Level)};


            Imports.TriggerConditions[] conditions = new Imports.TriggerConditions[] {
              new Imports.TriggerConditions(Imports.TriggerState.True,                      // Channel A
                                            Imports.TriggerState.DontCare,                  // Channel B
                                            Imports.TriggerState.DontCare,                  // Channel C
                                            Imports.TriggerState.DontCare,                  // Channel D
                                            Imports.TriggerState.DontCare,                  // external
                                            Imports.TriggerState.DontCare,                  // aux
                                            Imports.TriggerState.DontCare,                  // pwq
                                            Imports.TriggerState.True                       // digital
                                            )};

            Imports.ThresholdDirection[] directions = new Imports.ThresholdDirection[]
	                                        { Imports.ThresholdDirection.Rising,            // Channel A
                                            Imports.ThresholdDirection.None,                // Channel B
                                            Imports.ThresholdDirection.None,                // Channel C
                                            Imports.ThresholdDirection.None,                // Channel D
                                            Imports.ThresholdDirection.None,                // ext
                                            Imports.ThresholdDirection.None };              // aux


            Imports.DigitalChannelDirections[] digitalDirections = new Imports.DigitalChannelDirections[2];

            digitalDirections[0].DigiPort = Imports.DigitalChannel.PS2000A_DIGITAL_CHANNEL_1;
            digitalDirections[0].DigiDirection = Imports.DigitalDirection.PS2000A_DIGITAL_DIRECTION_RISING_OR_FALLING;

            digitalDirections[1].DigiPort = Imports.DigitalChannel.PS2000A_DIGITAL_CHANNEL_8;
            digitalDirections[1].DigiDirection = Imports.DigitalDirection.PS2000A_DIGITAL_DIRECTION_HIGH;


            Console.Write("Collect a block of data when value rises past {0}", (_scaleVoltages) ?
                          adc_to_mv(sourceDetails[0].ThresholdMajor, (int)_channelSettings[(int)Imports.Channel.ChannelA].range): sourceDetails[0].ThresholdMajor);
            
            Console.WriteLine("{0}", (_scaleVoltages) ? ("mV ") : ("ADC Counts "));
            Console.WriteLine("AND ");
            Console.WriteLine("Digital Channel  1   --- Rising Or Falling");
            Console.WriteLine("Digital Channel  8   --- High");
            Console.WriteLine("Other Digital Channels - Don't Care");
            Console.WriteLine();
            Console.WriteLine("Press a key to start...");
            WaitForKey();

            SetDefaults();

            /* Trigger enabled
            * Channel A
            * Rising edge
            * Threshold = 1000mV 
            * Digial
            * Ch13 High
            * Ch15 High */

            SetTrigger(sourceDetails, 1, conditions, 1, directions, null, 0, 0, 0, digitalDirections, 2);

            BlockDataHandler("Ten readings after trigger", 0, Imports.Mode.MIXED);

            DisableAnalogue();
        }


        /****************************************************************************
        * ORAnalogueDigitalTriggered
        *  this function demonstrates how to combine Digital AND Analogue triggers
        *  to collect a block of data.
        ****************************************************************************/
        void ORAnalogueDigitalTriggered()
        {
            Console.WriteLine("Analogue OR Digital Triggered Block");
            Console.WriteLine("Collect block of data when an Analogue OR Digital triggers occurs...");


            short triggerVoltage = mv_to_adc(1000, (short)_channelSettings[(int)Imports.Channel.ChannelA].range); // ChannelInfo stores ADC counts
            Imports.TriggerChannelProperties[] sourceDetails = new Imports.TriggerChannelProperties[] {
                new Imports.TriggerChannelProperties(triggerVoltage,
                                             256*10,
                                             triggerVoltage,
                                             256*10,
                                             Imports.Channel.ChannelA,
                                             Imports.ThresholdMode.Level)};


            Imports.TriggerConditions[] conditions = new Imports.TriggerConditions[2];
            
            conditions[0].ChannelA          =       Imports.TriggerState.True;
            conditions[0].ChannelB          =       Imports.TriggerState.DontCare;
            conditions[0].ChannelC          =       Imports.TriggerState.DontCare;
            conditions[0].ChannelD          =       Imports.TriggerState.DontCare;
            conditions[0].External          =       Imports.TriggerState.DontCare;
            conditions[0].Aux               =       Imports.TriggerState.DontCare;
            conditions[0].Pwq               =       Imports.TriggerState.DontCare;
            conditions[0].Digital           =       Imports.TriggerState.DontCare;

            conditions[1].ChannelA          =       Imports.TriggerState.DontCare;
            conditions[1].ChannelB          =       Imports.TriggerState.DontCare;
            conditions[1].ChannelC          =       Imports.TriggerState.DontCare;
            conditions[1].ChannelD          =       Imports.TriggerState.DontCare;
            conditions[1].External          =       Imports.TriggerState.DontCare;
            conditions[1].Aux               =       Imports.TriggerState.DontCare;
            conditions[1].Pwq               =       Imports.TriggerState.DontCare;
            conditions[1].Digital           =       Imports.TriggerState.True;


            Imports.ThresholdDirection[] directions = new Imports.ThresholdDirection[]
	                                        { Imports.ThresholdDirection.Rising,            // Channel A
                                            Imports.ThresholdDirection.None,                // Channel B
                                            Imports.ThresholdDirection.None,                // Channel C
                                            Imports.ThresholdDirection.None,                // Channel D
                                            Imports.ThresholdDirection.None,                // ext
                                            Imports.ThresholdDirection.None };              // aux


            Imports.DigitalChannelDirections[] digitalDirections = new Imports.DigitalChannelDirections[2];

            digitalDirections[0].DigiPort = Imports.DigitalChannel.PS2000A_DIGITAL_CHANNEL_1;
            digitalDirections[0].DigiDirection = Imports.DigitalDirection.PS2000A_DIGITAL_DIRECTION_RISING_OR_FALLING;

            digitalDirections[1].DigiPort = Imports.DigitalChannel.PS2000A_DIGITAL_CHANNEL_8;
            digitalDirections[1].DigiDirection = Imports.DigitalDirection.PS2000A_DIGITAL_DIRECTION_HIGH;


            Console.Write("Collect a block of data when value rises past {0}", (_scaleVoltages) ?
                        adc_to_mv(sourceDetails[0].ThresholdMajor, (int)_channelSettings[(int)Imports.Channel.ChannelA].range) : sourceDetails[0].ThresholdMajor);
            
            Console.WriteLine("{0}", (_scaleVoltages) ? ("mV ") : ("ADC Counts "));
            Console.WriteLine("OR");
            Console.WriteLine("Digital Channel  1   --- Rising Or Falling");
            Console.WriteLine("Digital Channel  8   --- High");
            Console.WriteLine("Other Digital Channels - Don't Care");
            Console.WriteLine();
            Console.WriteLine("Press a key to start...");
            WaitForKey();

            SetDefaults();

            /* Trigger enabled
             * Channel A
             * Rising edge
             * Threshold = 1000mV 
             * Digial
             * Ch13 High
             * Ch15 High */

            SetTrigger(sourceDetails, 1, conditions, 2, directions, null, 0, 0, 0, digitalDirections, 2);

            BlockDataHandler("Ten readings after trigger", 0, Imports.Mode.MIXED);

            DisableAnalogue();
        }


        /****************************************************************************
         * DigitalStreamingImmediate
         * Streams data from the digital ports with triggering disabled
         ***************************************************************************/
        void DigitalStreamingImmediate()
        {
            Console.WriteLine("Digital Streaming Immediate....");
            Console.WriteLine("Press a key to start");
            WaitForKey();

            SetTrigger(null, 0, null, 0, null, null, 0, 0, 0, null, 0);

            StreamDataHandler(0, Imports.Mode.DIGITAL);
        }

        /****************************************************************************
        * DigitalStreamingImmediate
        * Streams data from the digital ports with triggering disabled
        ***************************************************************************/
        void DigitalStreamingAggregated()
        {
            Console.WriteLine("Digital Streaming with Aggregation....");
            Console.WriteLine("Press a key to start");
            WaitForKey();

            SetTrigger(null, 0, null, 0, null, null, 0, 0, 0, null, 0);

            StreamDataHandler(0, Imports.Mode.AGGREGATED);
        }


        /*************************************************************************************
       * SetSignalGenerator
       *  this function demonstrates how to use the Signal Generator & 
       *  (where supported) AWG files (Values -32768 .. 32767, up to 8192 lines)
       *  
       **************************************************************************************/
        void SetSignalGenerator()
        {
            Imports.WaveType waveform = Imports.WaveType.PS2000A_DC_VOLTAGE;
            char ch;
            uint pkpk = 1000000;
            uint waveformSize = 0;
            Imports.ExtraOperations operation = Imports.ExtraOperations.PS2000A_ES_OFF;
            string fileName;
            int offset = 0;
            uint frequency = 1000;
            short[] arbitraryWaveform = new short[8192];
            short status;
            string lines = string.Empty;
            int i = 0;


            do
            {
                Console.WriteLine("");
                Console.WriteLine("Signal Generator\n================\n");
                Console.WriteLine("0:\tSINE      \t6:\tGAUSSIAN");
                Console.WriteLine("1:\tSQUARE    \t7:\tHALF SINE");
                Console.WriteLine("2:\tTRIANGLE  \t8:\tDC VOLTAGE");
                Console.WriteLine("3:\tRAMP UP   \t9:\tWHITE NOISE");
                Console.WriteLine("4:\tRAMP DOWN");
                Console.WriteLine("5:\tSINC");
                Console.WriteLine( "A:\tAWG WAVEFORM");
                Console.WriteLine("X:\tSigGen Off");
                Console.WriteLine("");

                ch = Console.ReadKey(true).KeyChar;

                if (ch >= '0' && ch <= '9')
                    waveform = (Imports.WaveType)((short)(ch - '0'));
                else
                    ch = char.ToUpper(ch);
            }
            while (ch != 'A' && ch != 'X' && (ch < '0' || ch > '9'));


            if (ch == 'X')				// If we're going to turn off siggen
            {
                Console.WriteLine("Signal generator Off");
                waveform = Imports.WaveType.PS2000A_DC_VOLTAGE;	
                pkpk = 0;				// 0V
                waveformSize = 0;
                operation = Imports.ExtraOperations.PS2000A_ES_OFF;
            }
            else
                if (ch == 'A')		// Set the AWG
                {
                    waveformSize = 0;

                    Console.WriteLine("Select a waveform file to load: ");
                    fileName = Console.ReadLine();

                    // Open file & read in data - one number per line (at most 8192 lines), with values in (0..4095)

                    StreamReader sr;
                    try
                    {
                        sr = new StreamReader(fileName);
                    }
                    catch (FileNotFoundException)
                    {
                        Console.WriteLine("Cannot open file.");
                        return;
                    }


                    while (((lines = sr.ReadLine()) != null) && i < 8192)
                    {
                        try
                        {
                            arbitraryWaveform[i++] = short.Parse(lines);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Error: " + e.Message);
                            sr.Close();
                            return;
                        }
                    }
                    sr.Close();


                    waveformSize =(uint)(arbitraryWaveform.Length);
                }
                else			// Set one of the built in waveforms
                {
                    switch ((int)(waveform))
                    {
                        case 8:
                            do
                            {
                                Console.WriteLine("Enter offset in uV: (0 to 2500000)"); // Ask user to enter DC offset level;

                                try
                                {
                                    offset = Int32.Parse(Console.ReadLine());
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine("Error: " + e.Message);
                                }
                            } while (offset < 0 || offset > 10000000);
                            operation = Imports.ExtraOperations.PS2000A_ES_OFF;
                            break;

                        case 9:
                            operation = Imports.ExtraOperations.PS2000A_WHITENOISE;
                            break;

                        default:
                            operation = Imports.ExtraOperations.PS2000A_ES_OFF;
                            offset = 0;
                            break;
                    }
                }

            if ((short)(waveform) < 8 || (ch == 'A'))				// Find out frequency if required
            {
                do
                {
                    Console.WriteLine("Enter frequency in Hz: (1 to 10000000)"); // Ask user to enter signal frequency;
                    try
                    {
                        frequency = UInt32.Parse(Console.ReadLine());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error: " + e.Message);
                    }
                } while (frequency <= 0 || frequency > 10000000);
            }

            if (waveformSize > 0)
            {
                uint phase = 0;
                Imports.SigGenFrequencyToPhase(_handle, frequency, Imports.IndexMode.PS2000A_SINGLE, waveformSize, out phase);

                status = Imports.SetSigGenArbitray(_handle,
                                                    0,
                                                    1000000,
                                                    (uint)phase,
                                                    (uint)phase,
                                                    0,
                                                    0,
                                                    arbitraryWaveform,
                                                    (int)waveformSize,
                                                    0,
                                                    0,
                                                    0,
                                                    0,
                                                    0,
                                                    0,
                                                    0,
                                                    0);

                Console.WriteLine(status != 0 ? "SetSigGenArbitrary: Status Error 0x%x " : "", status);		// If status != 0, show the error
            }
            else
            {
                status = Imports.SetSigGenBuiltIn(_handle, offset, pkpk, waveform, (float)frequency, (float)frequency, 0, 0, 0, operation, 0, 0, 0, 0, 0);
                Console.WriteLine(status != 0 ? "SetSigGenBuiltIn: Status Error 0x%x " : "", status);		// If status != 0, show the error
            }
        }

        /****************************************************************************
         * DigitalMenu - Only shown for the MSO scopes
         ****************************************************************************/
        public void DigitalMenu()
        {
            char ch;

            DisableAnalogue();                  // Disable the analogue ports
            SetDigitals();                      // Enable digital ports & set logic level

            ch = ' ';
            while (ch != 'X')
            {
                Console.WriteLine();
                Console.WriteLine("Digital Port Menu");
                Console.WriteLine();
                Console.WriteLine("B - Digital Block Immediate");
                Console.WriteLine("T - Digital Block Triggered");
                Console.WriteLine("A - Analogue 'AND' Digital Triggered Block");
                Console.WriteLine("O - Analogue 'OR'  Digital Triggered Block");
                Console.WriteLine("S - Digital Streaming Mode");
                Console.WriteLine("V - Digital Streaming Aggregated");
                Console.WriteLine("X - Return to previous menu");
                Console.WriteLine();
                Console.WriteLine("Operation:");

                ch = char.ToUpper(Console.ReadKey(true).KeyChar);

                Console.WriteLine("\n\n");
                switch (ch)
                {
                    case 'B':
                        DigitalBlockImmediate();
                        break;

                    case 'T':
                        DigitalBlockTriggered();
                        break;

                    case 'A':
                        ANDAnalogueDigitalTriggered();
                        break;

                    case 'O':
                        ORAnalogueDigitalTriggered();
                        break;

                    case 'S':
                        DigitalStreamingImmediate();
                        break;

                    case 'V':
                        DigitalStreamingAggregated();
                        break;
                }
            }
            DisableDigital();       // disable the digital ports when we're finished
        }

        /****************************************************************************
        * Run - show menu and call user selected options
        ****************************************************************************/
        public void Run()
        {
            // setup devices
            GetDeviceInfo();
            _timebase = 1;

            _firstRange = Imports.Range.Range_100MV;
            _lastRange = Imports.Range.Range_20V;
            _channelCount = DUAL_SCOPE;
            _channelSettings = new ChannelSettings[MAX_CHANNELS];

            for (int i = 0; i < MAX_CHANNELS; i++)
            {
                _channelSettings[i].enabled = true;
                _channelSettings[i].DCcoupled = true;
                _channelSettings[i].range = Imports.Range.Range_2V;
            }

            // main loop - read key and call routine
            char ch = ' ';
            while (ch != 'X')
            {
                DisplaySettings();

                Console.WriteLine("");
                Console.WriteLine("B - Immediate Block              V - Set voltages");
                Console.WriteLine("T - Triggered Block              I - Set timebase");
                Console.WriteLine("R - Rapid Block                  A - ADC counts/mV");
                Console.WriteLine("S - Immediate Streaming          G - Signal generator");
                Console.WriteLine("W - Triggered Streaming");
                Console.WriteLine(_digitalPorts > 0? "D - Digital Ports Menu" : "");
                Console.WriteLine("                                 X - exit");
                Console.WriteLine("Operation:");

                ch = char.ToUpper(Console.ReadKey(true).KeyChar);

                Console.WriteLine("\n");
                switch (ch)
                {
                    case 'B':
                        CollectBlockImmediate();
                        break;

                    case 'T':
                        CollectBlockTriggered();
                        break;

                    case 'R':
                        CollectBlockRapid();
                        break;

                    case 'S':
                        CollectStreamingImmediate();
                        break;

                    case 'W':
                        CollectStreamingTriggered();
                        break;

                    case 'V':
                        SetVoltages();
                        break;

                    case 'I':
                        SetTimebase();
                        break;

                    case 'A':
                        _scaleVoltages = !_scaleVoltages;
                        break;

                    case 'G':
                        SetSignalGenerator();
                        break;


                    case 'D':
                        if (_digitalPorts > 0)
                            DigitalMenu();
                        break;

                    case 'X':
                        /* Handled by outer loop */
                        break;

                    default:
                        Console.WriteLine("Invalid operation");
                        break;
                }
            }
        }


        private PS2000ACSConsole(short handle)
        {
            _handle = handle;
        }

        static void Main()
        {
            Console.WriteLine("C# PS2000A driver example program");


            //open unit and show splash screen
            Console.WriteLine("\n\nOpening the device...");
            short handle;

            short status = Imports.OpenUnit(out handle, null);
            Console.WriteLine("Handle: {0}", handle);

            if (status != 0)
            {
                Console.WriteLine("Unable to open device");
                Console.WriteLine("Error code : {0}", status);
                WaitForKey();
            }
            else
            {
                Console.WriteLine("Device opened successfully\n");

                PS2000ACSConsole consoleExample = new PS2000ACSConsole(handle);
                consoleExample.Run();

                Imports.CloseUnit(handle);
            }
        }
    }
}
