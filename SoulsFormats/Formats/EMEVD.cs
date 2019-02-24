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

        internal int HeaderSize => IsDS1 ? 84 : 148;
        internal int EventSize => IsDS1 ? 28 : 48;
        internal int InstructionSize => IsDS1 ? 24 : 32;
        internal int LayerSize => IsDS1 ? 20 : 32;
        internal int ParameterSize => IsDS1 ? 20 : 32;
        internal int LinkedFileSize => IsDS1 ? 4 : 8;


        public Event GetEvent(int i) => Events.Find(evt => evt.ID == i);

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

            int uintW() => !IsDS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();
            long zeroW() => !IsDS1 ? br.AssertInt64(0) : br.AssertInt32(0);

            uintW();
            int evtCount = uintW();
            int evtOffset = uintW();
            int insCount = uintW();
            int insOffset = uintW();

            zeroW();

            int layOffset = uintW();
            int layCount = uintW();
            if (layOffset != uintW()) Console.WriteLine("WARNING: Event layer table offset inconsistent.");

            int prmCount = uintW();
            int prmOffset = uintW();
            int lnkCount = uintW();
            int lnkOffset = uintW();
            int argLength = uintW();
            int argOffset = uintW();
            int strLength = uintW();
            int strOffset = uintW();

            if (Game == Game.DS1) zeroW();
            #endregion

            /* We jump around the file bceause reading the instructions and parameters
             * first makes it easier to process events.
             */

            Console.WriteLine("Reading instructions...");
            br.Position = insOffset;
            for (int i = 0; i < insCount; i++) ReadInstructions.Add(new Instruction(br, this));

            Console.WriteLine("Reading layers...");
            br.Position = layOffset;
            for (int i = 0; i < layCount; i++) ReadLayers.Add(new Layer(br, Game));

            Console.WriteLine("Reading argument data...");
            br.Position = argOffset;
            ReadArgData = new List<byte>(br.ReadBytes(argLength));

            Console.WriteLine("Reading parameters...");
            br.Position = prmOffset;
            for (int i = 0; i < prmCount; i++) ReadParameters.Add(new Parameter(br, Game));

            Console.WriteLine("Reading linked files...");
            br.Position = lnkOffset;
            for (int i = 0; i < lnkCount; i++) ReadLinkedFiles.Add(new LinkedFile(br, Game));

            Console.WriteLine("Reading string data...");
            br.Position = strOffset;
            ReadStringData = new List<byte>(br.ReadBytes(strLength));

            Console.WriteLine("Reading events...");
            br.Position = evtOffset;
            for (int i = 0; i < evtCount; i++) Events.Add(new Event(br, this));
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

            void uintW(int i)
            {
                if (IsDS1) bw.WriteUInt32((uint)i);
                else bw.WriteUInt64((ulong)i);
            }

            void resUintW(string name)
            {
                if (IsDS1) bw.ReserveUInt32(name);
                else bw.ReserveUInt64(name);
            }

            void fillUintW(string name, int value)
            {
                if (IsDS1) bw.FillUInt32(name, (uint) value);
                else bw.FillUInt64(name, (ulong) value);
            }

            //reserve values in header
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

            //write tables
            foreach (var evt in Events) evt.Write(bw);
            foreach (var ins in WriteInstructions) ins.Write(bw);
            foreach (var lay in WriteLayers) lay.Write(bw);
            bw.WriteBytes(WriteArgData.ToArray());
            foreach (var par in WriteParameters) par.Write(bw);
            foreach (var lnk in WriteParameters) lnk.Write(bw);
            bw.WriteBytes(WriteStringData.ToArray());

            //fill header values
            int fileSize = HeaderSize;
            fileSize += Events.Count * EventSize;
            fileSize += WriteInstructions.Count * InstructionSize;
            fileSize += WriteLayers.Count * LayerSize;
            fileSize += WriteParameters.Count * ParameterSize;
            fileSize += WriteLinkedFiles.Count * LinkedFileSize;
            fileSize += WriteArgData.Count + (IsDS1 ? 4 : 0);
            fileSize += WriteStringData.Count;

            int eventTableOffset = HeaderSize;
            int instructionTableOffset = eventTableOffset + Events.Count * EventSize;
            int eventLayerTableOffset = instructionTableOffset + WriteInstructions.Count * InstructionSize;
            int argDataOffset = eventLayerTableOffset + WriteLayers.Count * LayerSize;
            int paramTableOffset = argDataOffset + WriteArgData.Count + (IsDS1 ? 4 : 0);
            int linkedFileTableOffset = paramTableOffset + WriteParameters.Count * ParameterSize;
            int stringDataOffset = linkedFileTableOffset + WriteLinkedFiles.Count * LinkedFileSize;

            fillUintW("fileSize", fileSize);
            fillUintW("eventCount", Events.Count);
            fillUintW("eventTableOffset", eventTableOffset);
            fillUintW("instructionCount", WriteInstructions.Count);
            fillUintW("instructionTableOffset", instructionTableOffset);
            fillUintW("eventLayerTableOffset", eventLayerTableOffset);
            fillUintW("layerCount", WriteLayers.Count);
            fillUintW("paramCount", WriteParameters.Count);
            fillUintW("paramTableOffset", paramTableOffset);
            fillUintW("linkedFileCount", WriteLinkedFiles.Count);
            fillUintW("linkedFileTableOffset", linkedFileTableOffset);
            fillUintW("argDataLength", WriteArgData.Count);
            fillUintW("argDataOffset", argDataOffset);
            fillUintW("stringDataLength", WriteStringData.Count);
            fillUintW("stringDataOffset", stringDataOffset);
        }


        #region Nested Classes

        public class Event
        {
            public EMEVD File { get; }
            public int ID { get; set; }
            public BonfireHandler BonfireHandler { get; set; }

            public List<Instruction> Instructions = new List<Instruction>();
            public List<Parameter> Parameters = new List<Parameter>();

            public int InstructionOffset => File.ReadInstructions.FindIndex(i => i == Instructions[0]) * File.InstructionSize;
            public int ParameterOffset => File.ReadParameters.FindIndex(p => p == Parameters[0]) * File.ParameterSize;

            public Event(int id, EMEVD emevd)
            {
                ID = id;
                File = emevd;
                BonfireHandler = BonfireHandler.Normal;
            }

            public Event(BinaryReaderEx br, EMEVD emevd)
            {

                File = emevd;
                int uintW() => File.IsDS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();
                int sintW() => File.IsDS1 ? (int)br.ReadInt64() : br.ReadInt32();

                ID = uintW();

                int instructionCount = uintW();
                int instructionOffset = uintW();
                int insStart = instructionOffset / File.InstructionSize;
                for (int i = insStart; i < instructionCount; i++)
                {
                    Instructions.Add(File.ReadInstructions[i]);
                }

                int paramCount = uintW();
                int paramOffset = File.Game == Game.BB ? (int) br.ReadUInt32() : sintW();
                if (File.Game == Game.BB) br.AssertInt32(0);

                int parStart = paramOffset / (File.IsDS1 ? 20 : 32);
                for (int i = parStart; i < paramCount; i++)
                {
                    Parameters.Add(File.ReadParameters[i]);
                }


                BonfireHandler = (BonfireHandler)br.AssertInt32(0x00000000, 0x00000001, 0x00000002);

                br.AssertInt32(0);
            }

            public void Write(BinaryWriterEx bw)
            {
                void uintW(int i)
                {
                    if (File.IsDS1) bw.WriteUInt32((uint)i);
                    else bw.WriteUInt64((ulong)i);
                }

                uintW(ID);
                uintW(Instructions.Count);
                uintW(File.ReadInstructions.Count * File.InstructionSize);
                uintW(Parameters.Count);
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
                File = emevd;

                InstructionClass = br.ReadUInt32();
                InstructionIndex = br.ReadUInt32();
                ArgLength = File.IsDS1 ? (int)br.ReadUInt32() : (int)br.ReadUInt64();
                ArgOffset = File.IsDS1 ? (int)br.ReadInt32() : (int)br.ReadInt64();
                if (!File.IsDS1) br.AssertInt32(0);

                EventLayerOffset = File.IsDS1 ? (int)br.ReadInt64() : (int)br.ReadInt32();
                if (File.Game != Game.DS3) br.AssertInt32(0);
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
        }

        #endregion

    }
}
