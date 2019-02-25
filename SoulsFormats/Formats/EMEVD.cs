using System;
using System.Collections.Generic;

namespace SoulsFormats.Formats
{
    public enum Game : uint { DS1, BB, DS3 }

    public enum BonfireHandler : uint { Normal = 0, Restart = 1, End = 2 };

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

            /* Read different values depending on if the game targets 32-bit or 64-bit
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

            br.Position = insOffset;
            for (long i = 0; i < insCount; i++)
                ReadInstructions.Add(new Instruction(br, this));

            br.Position = layOffset;
            for (long i = 0; i < layCount; i++)
                ReadLayers.Add(new Layer(br, Game));

            br.Position = argOffset;
            ReadArgData = new List<byte>(br.ReadBytes((int) argLength));

            br.Position = prmOffset;
            for (long i = 0; i < prmCount; i++)
                ReadParameters.Add(new Parameter(br, Game));

            br.Position = lnkOffset;
            for (long i = 0; i < lnkCount; i++)
                ReadLinkedFiles.Add(new LinkedFile(br, Game));

            br.Position = strOffset;
            ReadStringData = new List<byte>(br.ReadBytes((int) strLength));

            br.Position = evtOffset;
            for (long i = 0; i < evtCount; i++)
                Events.Add(new Event(br, this));
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

            void uintW(long i)
            {
                if (IsDS1) bw.WriteUInt32((uint) i);
                else bw.WriteUInt64((ulong) i);
            }

            void resUintW(string name)
            {
                if (IsDS1) bw.ReserveUInt32(name);
                else bw.ReserveUInt64(name);
            }

            void fillUintW(string name)
            {
                if (IsDS1) bw.FillUInt32(name, (uint) bw.Position);
                else bw.FillUInt64(name, (ulong) bw.Position);
            }

            //write header
            resUintW("fileSize");
            uintW(Events.Count);
            resUintW("eventTableOffset");
            uintW(WriteInstructions.Count);
            resUintW("instructionTableOffset");
            uintW(0);
            resUintW("eventLayerTableOffset");
            uintW(WriteLayers.Count);
            resUintW("eventLayerTableOffset");
            uintW(WriteParameters.Count);
            resUintW("paramTableOffset");
            uintW(WriteLinkedFiles.Count);
            resUintW("linkedFileTableOffset");
            uintW(WriteArgData.Count + (IsDS1 ? 4 : 0));
            resUintW("argDataOffset");
            uintW(WriteStringData.Count);
            resUintW("stringDataOffset");

            //write events
            fillUintW("eventTableOffset");
            foreach (var evt in Events) evt.Write(bw);

            //write instructions
            fillUintW("instructionTableOffset");
            foreach (var ins in WriteInstructions) ins.Write(bw);

            //write layers
            fillUintW("instructionTableOffset");
            foreach (var lay in WriteLayers) lay.Write(bw);

            //write argument data
            fillUintW("argDataOffset");
            bw.WriteBytes(WriteArgData.ToArray());

            //write parameters
            fillUintW("paramTableOffset");
            foreach (var par in WriteParameters) par.Write(bw);

            //write linked files
            fillUintW("linkedFileTableOffset");
            foreach (var lnk in WriteLinkedFiles) lnk.Write(bw);

            //write string data
            fillUintW("stringDataOffset");
            bw.WriteBytes(WriteStringData.ToArray());

            //write file size
            fillUintW("fileSize");
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
                long uintW() => !File.IsDS1 ? (long) br.ReadUInt64() : br.ReadUInt32();
                long sintW() => !File.IsDS1 ? br.ReadInt64() : br.ReadInt32();

                ID = uintW();

                long instructionCount = uintW();
                long instructionOffset = uintW();
                long insStart = instructionOffset / File.InstructionSize;
                for (long i = insStart; i < insStart + instructionCount; i++)
                    Instructions.Add(File.ReadInstructions[(int) i]);

                long paramCount = uintW();
                long paramOffset = File.Game == Game.BB ? br.ReadUInt32() : sintW();
                if (File.Game == Game.BB) br.AssertUInt32(0);

                long parStart = paramOffset / (File.IsDS1 ? 20 : 32);
                for (long i = parStart; i < parStart + paramCount; i++)
                    Parameters.Add(File.ReadParameters[(int) i]);

                BonfireHandler = br.ReadEnum32<BonfireHandler>();
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
                uintW(File.WriteInstructions.Count * File.InstructionSize);

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

                uintW(File.WriteParameters.Count * File.ParameterSize);
                bw.WriteUInt32((uint) BonfireHandler);
                bw.WriteInt32(0);

                foreach (var i in Instructions)
                    File.WriteInstructions.Add(i);
                foreach (var p in Parameters)
                    File.WriteParameters.Add(p);
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
            public long ArgLength;
            public long ArgOffset;
            public long EventLayerOffset;

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

            public long InstructionNumber;
            public long DestinationStartByte;
            public long SourceStartByte;
            public long Length;

            public Parameter (BinaryReaderEx br, Game g)
            {
                Game = g;

                long uintW() => Game != Game.DS1 ? (long) br.ReadUInt64() : br.ReadUInt32();

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

            long FileNameOffset;

            public LinkedFile (BinaryReaderEx br, Game g)
            {
                Game = g;
                FileNameOffset = (long)(Game != Game.DS1 ? br.ReadUInt64() : br.ReadUInt32());
            }

            public void Write(BinaryWriterEx bw)
            {

            }
        }

        #endregion

    }
}
