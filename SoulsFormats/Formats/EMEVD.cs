using System;
using System.Collections.Generic;

namespace SoulsFormats.Formats
{
    public enum Game { DS1 = 0, BB = 1, DS3 = 2 }

    public enum BonfireHandler { Normal = 0x00000000, Restart = 0x00000001, End = 0x00000002 }

    public class EMEVD : SoulsFile<EMEVD>
    {
        public Game Game { get; set; }
        private bool IsDS1 => Game == Game.DS1;

        public List<Event> Events = new List<Event>();

        //for managing data when reading from a file
        internal List<LinkedFile> ReadLinkedFiles = new List<LinkedFile>();
        internal List<byte> ReadStringData = new List<byte>();
        internal List<byte> ReadArgData = new List<byte>();
        internal List<Layer> ReadLayers = new List<Layer>();
        internal List<Parameter> ReadParameters = new List<Parameter>();
        internal List<Instruction> ReadInstructions = new List<Instruction>();

        //for managing data when exporting
        internal List<Instruction> WriteInstructions = new List<Instruction>();
        internal List<LinkedFile> WriteLinkedFiles = new List<LinkedFile>();
        internal List<byte> WriteStringData = new List<byte>();
        internal List<byte> WriteArgData = new List<byte>();
        internal List<Layer> WriteLayers = new List<Layer>();
        internal List<Parameter> WriteParameters = new List<Parameter>();

        internal long HeaderSize => IsDS1 ? 84 : 148;
        internal long EventSize => IsDS1 ? 28 : 48;
        internal long InstructionSize => IsDS1 ? 24 : 32;
        internal long LayerSize => IsDS1 ? 20 : 32;
        internal long ParameterSize => IsDS1 ? 20 : 32;
        internal long LinkedFileSize => IsDS1 ? 4 : 8;


        public Event GetEvent(long i) => Events.Find(evt => evt.ID == i);

        internal override bool Is(BinaryReaderEx br) => br.GetASCII(0, 4) == "EVD\0";

        internal override void Read(BinaryReaderEx br)
        {
            #region Header

            br.AssertASCII("EVD\0");
            uint v1 = br.ReadUInt32();
            uint v2 = br.ReadUInt32();

            if (v1 == 0x00000000 && v2 == 0x000000CC) Game = Game.DS1;
            else if (v1 == 0x0000FF00 && v2 == 0x000000CC) Game = Game.BB;
            else if (v1 == 0x0001FF00 && v2 == 0x000000CD) Game = Game.DS3;
            else throw new Exception("Could not detect game type from header.");

            /* rRead different values depending on if the game targets 32-bit or 64-bit
             * and return them as int. Convert them back to proper types on write.
             */

            long uintW() => !IsDS1 ? (long) br.ReadUInt64() : br.ReadUInt32();
            long zeroW() => !IsDS1 ? br.AssertInt64(0) : br.AssertInt32(0);

            uintW();
            long evtCount = uintW();
            long evtOffset = uintW();
            long insCount = uintW();
            long insOffset = uintW();

            zeroW();

            long layOffset = uintW();
            long layCount = uintW();
            if (layOffset != uintW()) Console.WriteLine("WARNING: Event layer table offset inconsistent.");

            long prmCount = uintW();
            long prmOffset = uintW();
            long lnkCount = uintW();
            long lnkOffset = uintW();
            long argLength = uintW();
            long argOffset = uintW();
            long strLength = uintW();
            long strOffset = uintW();

            if (Game == Game.DS1) zeroW();
            #endregion

            /* We jump around the file bceause reading the instructions and parameters
             * first makes it easier to process events.
             */

            Console.WriteLine("Reading instructions...");
            br.Position = (int) insOffset;
            for (long i = 0; i < insCount; i++) ReadInstructions.Add(new Instruction(br, this));

            Console.WriteLine("Reading layers...");
            br.Position = (int) layOffset;
            for (long i = 0; i < layCount; i++) ReadLayers.Add(new Layer(br, Game));

            Console.WriteLine("Reading argument data...");
            br.Position = (int) argOffset;
            ReadArgData = new List<byte>(br.ReadBytes((int) argLength));

            Console.WriteLine("Reading parameters...");
            br.Position = (int) prmOffset;
            for (long i = 0; i < prmCount; i++) ReadParameters.Add(new Parameter(br, Game));

            Console.WriteLine("Reading linked files...");
            br.Position = (int) lnkOffset;
            for (long i = 0; i < lnkCount; i++) ReadLinkedFiles.Add(new LinkedFile(br, Game));

            Console.WriteLine("Reading string data...");
            br.Position = (int) strOffset;
            ReadStringData = new List<byte>(br.ReadBytes((int) strLength));

            Console.WriteLine("Reading events...");
            br.Position = (int) evtOffset;
            for (long i = 0; i < evtCount; i++) Events.Add(new Event(br, this));
        }

        internal override void Write(BinaryWriterEx bw)
        {
            WriteArgData.Clear();
            WriteInstructions.Clear();
            WriteLayers.Clear();
            WriteLinkedFiles.Clear();
            WriteParameters.Clear();
            WriteStringData.Clear();

            bw.WriteASCII("EVD\0");
            if (Game == Game.DS1)
            {
                bw.WriteUInt32(0x00000000);
                bw.WriteUInt32(0x000000CC);
            } else if (Game == Game.BB)
            {
                bw.WriteUInt32(0x0000FF00);
                bw.WriteUInt32(0x000000CC);
            } else
            {
                bw.WriteUInt32(0x0001FF00);
                bw.WriteUInt32(0x000000CD);
            }

            void uintW(ulong i)
            {
                if (IsDS1) bw.WriteUInt32((uint) i);
                else bw.WriteUInt64(i);
            }

            void resUintW(string name)
            {
                if (IsDS1) bw.ReserveUInt32(name);
                else bw.ReserveUInt64(name);
            }

            void fillUintW(string name, long value)
            {
                if (IsDS1) bw.FillUInt32(name, (uint) value);
                else bw.FillUInt64(name, (ulong) value);
            }

            //write header
            resUintW("fileSize");
            resUintW("eventCount");
            resUintW("eventTableOffset");
            resUintW("instructionCount");
            resUintW("instructionTableOffset");
            uintW(0);
            resUintW("eventLayerTableOffset");
            resUintW("layerCount");
            resUintW("eventLayerTableOffset");
            resUintW("paramCount");
            resUintW("paramTableOffset");
            resUintW("linkedFileCount");
            resUintW("linkedFileTableOffset");
            resUintW("argDataLength");
            resUintW("argDataOffset");
            resUintW("stringDataLength");
            resUintW("stringDataOffset");

            //write events
            fillUintW("eventTableOffset", bw.Position);
            foreach (var evt in Events) evt.Write(bw);
            fillUintW("eventCount", Events.Count);

            //write instructions
            fillUintW("instructionTableOffset", bw.Position);
            foreach (var ins in WriteInstructions) ins.Write(bw);
            fillUintW("instructionCount", WriteInstructions.Count);

            //write layers
            fillUintW("instructionTableOffset", bw.Position);
            foreach (var lay in WriteLayers) lay.Write(bw);
            fillUintW("layerCount", WriteLayers.Count);

            //write argument data
            fillUintW("argDataOffset", bw.Position);
            bw.WriteBytes(WriteArgData.ToArray());
            fillUintW("argDataLength", WriteArgData.Count + (IsDS1 ? 4 : 0));

            //write parameters
            fillUintW("paramTableOffset", bw.Position);
            foreach (var par in WriteParameters) par.Write(bw);
            fillUintW("paramCount", WriteParameters.Count);

            //write linked files
            fillUintW("linkedFileTableOffset", bw.Position);
            foreach (var lnk in WriteLinkedFiles) lnk.Write(bw);
            bw.WriteBytes(WriteStringData.ToArray());

            //write string data
            fillUintW("stringDataOffset", bw.Position);
            bw.WriteBytes(WriteStringData.ToArray());
            fillUintW("stringDataLength", WriteStringData.Count);

            //write file size
            fillUintW("fileSize", bw.Position);
        }


        #region Nested Classes

        public class Event
        {
            public EMEVD File { get; }
            public long ID { get; set; }
            public BonfireHandler BonfireHandler { get; set; }

            public List<Instruction> Instructions = new List<Instruction>();
            public List<Parameter> Parameters = new List<Parameter>();

            public long InstructionOffset => File.ReadInstructions.FindIndex(i => i == Instructions[0]) * File.InstructionSize;
            public long ParameterOffset => Parameters.Count == 0 ? -1 : File.ReadParameters.FindIndex(p => p == Parameters[0]) * File.ParameterSize;

            public Event(long id, EMEVD emevd)
            {
                ID = id;
                File = emevd;
                BonfireHandler = BonfireHandler.Normal;
            }

            public Event(BinaryReaderEx br, EMEVD emevd)
            {

                File = emevd;
                long uintW() => File.IsDS1 ? (long) br.ReadUInt64() : br.ReadUInt32();
                long sintW() => File.IsDS1 ? br.ReadInt64() : br.ReadInt32();

                ID = uintW();

                long instructionCount = uintW();
                long instructionOffset = uintW();
                long insStart = instructionOffset / File.InstructionSize;
                for (long i = insStart; i < instructionCount; i++)
                {
                    Instructions.Add(File.ReadInstructions[(int) i]);
                }

                long paramCount = uintW();
                long paramOffset = File.Game == Game.BB ? br.ReadUInt32() : sintW();
                if (File.Game == Game.BB) br.AssertUInt32(0);

                long parStart = paramOffset / (File.IsDS1 ? 20 : 32);
                for (long i = parStart; i < paramCount; i++)
                {
                    Parameters.Add(File.ReadParameters[(int) i]);
                }

                BonfireHandler = (BonfireHandler)br.AssertInt32(0x00000000, 0x00000001, 0x00000002);
                br.AssertUInt32(0);
            }

            public void Write(BinaryWriterEx bw)
            {
                void uintW(long i)
                {
                    if (File.IsDS1) bw.WriteUInt32((uint)i);
                    else bw.WriteUInt64((ulong) i);
                }

                uintW(ID);
                uintW(Instructions.Count);
                uintW(File.ReadInstructions.Count * File.InstructionSize);

                if (Parameters.Count == 0)
                {
                    if (File.Game == Game.DS1) bw.WriteInt32(-1);
                    else if (File.Game == Game.BB)
                    {
                        bw.WriteInt32(-1);
                        bw.WriteInt32(0);
                    }
                    else bw.WriteInt64(-1);

                }
                else uintW(Parameters.Count);
                uintW(File.ReadParameters.Count * File.ParameterSize);
                bw.WriteUInt32((uint)BonfireHandler);
                bw.WriteInt32(0);

                foreach (var i in Instructions) File.WriteInstructions.Add(i);
                foreach (var p in Parameters) File.WriteParameters.Add(p);
            }
        }

        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */
        /* EVERYTHING BELOW HERE IS UNFINISHED */

        public class Instruction
        {
            EMEVD File;

            public uint InstructionClass;
            public uint InstructionIndex;
            public int ArgLength;
            public int ArgOffset;
            public int EventLayerOffset;

            public Instruction(uint insClass, uint insIndex)
            {
                InstructionClass = insClass;
                InstructionIndex = insIndex;
            }

            public Instruction(BinaryReaderEx br, EMEVD emevd)
            {

            }

            public void Write(BinaryWriterEx bw)
            {

            }
        }

        public class Layer
        {
            Game Game;

            public uint LayerNum;

            public Layer(BinaryReaderEx br, Game g)
            {
                Game = g;

                br.AssertInt32(2);
                LayerNum = br.ReadUInt32();

                if (Game == Game.DS1)
                {
                    br.AssertUInt32(0);
                    br.AssertInt32(-1);
                    br.AssertUInt32(1);
                }
                else
                {
                    br.AssertUInt64(0);
                    br.AssertInt64(-1);
                    br.AssertUInt64(1);
                }
            }

            public void Write(BinaryWriterEx bw)
            {

            }
        }

        public class Parameter
        {
            public Game Game;

            public int InstructionNumber;
            public int DestinationStartByte;
            public int SourceStartByte;
            public int Length;

            public Parameter (BinaryReaderEx br, Game g)
            {
                Game = g;

                int uintW() => Game != Game.DS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();

                InstructionNumber = uintW();
                DestinationStartByte = uintW();
                SourceStartByte = uintW();
                Length = uintW();

                if (Game == Game.DS1) br.AssertUInt32(0);
            }

            public void Write (BinaryWriterEx bw)
            {

            }
        }

        public class LinkedFile
        {
            public Game Game;

            int FileNameOffset;

            public LinkedFile (BinaryReaderEx br, Game g)
            {
                Game = g;
                FileNameOffset = Game != Game.DS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();
            }

            public void Write(BinaryWriterEx bw)
            {

            }
        }

        #endregion

    }
}
