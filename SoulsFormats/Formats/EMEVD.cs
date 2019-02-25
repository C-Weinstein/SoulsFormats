using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SoulsFormats.Formats
{
    public enum Game : uint { DS1, BB, DS3 }

    public enum BonfireHandler : uint { Normal = 0, Restart = 1, End = 2 };

    public class EMEVD : SoulsFile<EMEVD>
    {
        public Game Game { get; set; }
        public List<Event> Events = new List<Event>();

        private bool IsDS1 => Game == Game.DS1;
        private EMEDF Documentation;

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
            if (Game == Game.DS1) Documentation = EMEDF.Read("ds1-common.emedf.json");
            else if (Game == Game.BB) Documentation = EMEDF.Read("bb-common.emedf.json");
            else if (Game == Game.DS3) Documentation = EMEDF.Read("ds3-common.emedf.json");
            
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
            br.Position = argOffset;
            ReadArgData = new List<byte>(br.ReadBytes((int)argLength));

            br.Position = layOffset;
            for (long i = 0; i < layCount; i++)
                ReadLayers.Add(new Layer(br, this));

            br.Position = insOffset;
            for (long i = 0; i < insCount; i++)
                ReadInstructions.Add(new Instruction(br, this));

            br.Position = prmOffset;
            for (long i = 0; i < prmCount; i++)
                ReadParameters.Add(new Parameter(br, this));

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

        public class Instruction
        {
            EMEVD File;

            public uint InstructionClass;
            public uint InstructionIndex;
            public EMEDF.ArgDoc[] ArgDocs;
            public Layer EventLayer = null;
            public dynamic[] Arguments;

            public Instruction(uint insClass, uint insIndex, params dynamic[] args)
            {
                InstructionClass = insClass;
                InstructionIndex = insIndex;
                ArgDocs = File.Documentation[InstructionClass][InstructionIndex].Arguments;
                Arguments = new dynamic[ArgDocs.Length];
                if (args.Length > 0) SetArgs(args);
            }

            public void SetArgs(params dynamic[] args)
            {
                for (int i = 0; i < Arguments.Length && i < args.Length; i++)
                {
                    Arguments[i] = args[i];
                }
            }

            public Instruction(BinaryReaderEx br, EMEVD emevd)
            {
                File = emevd;

                InstructionClass = br.ReadUInt32();
                InstructionIndex = br.ReadUInt32();
                ArgDocs = File.Documentation[InstructionClass][InstructionIndex].Arguments;

                int argLength = File.IsDS1 ? (int)br.ReadUInt32() : (int)br.ReadUInt64();
                int argOffset = (int)br.ReadUInt32();
                if (!File.IsDS1) br.AssertInt32(0);

                List<byte> argData = File.ReadArgData.GetRange(argOffset, argLength);
                Arguments = ReadArgs(argData);

                long layerOffset = File.Game == Game.DS3 ? br.ReadInt64() : br.ReadInt32();
                if (File.Game != Game.DS3) br.AssertInt32(0);
                if (layerOffset != -1)
                {
                    int layerIndex = (int)(layerOffset / File.LayerSize);
                    EventLayer = File.ReadLayers[layerIndex];
                }
            }

            private dynamic[] ReadArgs(List<byte> argData)
            {
                List<dynamic> args = new List<dynamic>();

                var br = new BinaryReaderEx(false, argData.ToArray()); 
                foreach (var argDoc in ArgDocs)
                {
                    if (argDoc.Type == 0) {
                        args.Add(br.ReadByte());
                    } else if (argDoc.Type == 1)
                    {
                        br.Pad(2);
                        args.Add(br.ReadUInt16());
                    } else if (argDoc.Type == 2)
                    {
                        br.Pad(4);
                        args.Add(br.ReadUInt32());
                    } else if (argDoc.Type == 3)
                    {
                        args.Add(br.ReadByte());
                    }
                    else if (argDoc.Type == 4)
                    {
                        br.Pad(2);
                        args.Add(br.ReadInt16());
                    } else if (argDoc.Type == 5)
                    {
                        br.Pad(4);
                        args.Add(br.ReadInt32());
                    } else if (argDoc.Type == 6)
                    {
                        br.Pad(4);
                        args.Add(br.ReadSingle());
                    } else if (argDoc.Type == 8)
                    {
                        br.Pad(4);
                        args.Add(br.ReadUInt32());
                    }
                }

                return args.ToArray();
            }

            public void Write(BinaryWriterEx bw)
            {

                bw.WriteUInt32(InstructionClass);
                bw.WriteUInt32(InstructionIndex);

                for (int i = 0; i < ArgDocs.Length; i++)
                {
                    var doc = ArgDocs[i];
                    var arg = Arguments[i];

                    if (doc.Type == 0)
                    {
                        bw.WriteByte(arg);
                    } else if (doc.Type == 1)
                    {
                        bw.Pad(2);
                        bw.WriteUInt16(arg);
                    } else if (doc.Type == 2)
                    {
                        bw.Pad(4);
                        bw.WriteUInt32(arg);
                    } else if (doc.Type == 3)
                    {
                        bw.WriteByte(arg);
                    }
                    else if (doc.Type == 4)
                    {
                        bw.Pad(2);
                        bw.WriteInt16(arg);
                    }
                    else if (doc.Type == 5)
                    {
                        bw.Pad(4);
                        bw.WriteInt32(arg);
                    }
                    else if (doc.Type == 6)
                    {
                        bw.Pad(4);
                        bw.WriteSingle(arg);
                    }
                    else if (doc.Type == 8)
                    {
                        bw.Pad(4);
                        bw.WriteUInt32(arg);
                    }
                }
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


        public class Layer
        {
            public EMEVD File;

            public uint LayerNum;

            public Layer(BinaryReaderEx br, EMEVD emevd)
            {
                File = emevd;

                br.AssertInt32(2);
                LayerNum = br.ReadUInt32();

                if (File.IsDS1)
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
            public EMEVD File;

            public long InstructionNumber;
            public long DestinationStartByte;
            public long SourceStartByte;
            public long Length;

            public Parameter (BinaryReaderEx br, EMEVD emevd)
            {
                File = emevd;

                long uintW() => File.IsDS1 ? br.ReadUInt32() : (long) br.ReadUInt64();

                InstructionNumber = uintW();
                DestinationStartByte = uintW();
                SourceStartByte = uintW();
                Length = uintW();

                if (File.IsDS1) br.AssertUInt32(0);
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

        public class EMEDF
        {
            public ClassDoc this[uint i] => Classes.Find(c => c.Index == i);

            [JsonProperty(PropertyName = "unknown")]
            private long UNK;

            [JsonProperty(PropertyName = "main_classes")]
            private List<ClassDoc> Classes;

            [JsonProperty(PropertyName = "enums")]
            public EnumDoc[] Enums;

            public static EMEDF Read (string path)
            {
                string input = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<EMEDF>(input);
            }

            public class ClassDoc
            {
                [JsonProperty(PropertyName = "name")]
                public string Name { get; set; }

                [JsonProperty(PropertyName = "index")]
                public long Index { get; set; }

                [JsonProperty(PropertyName = "instrs")]
                public List<InstrDoc> Instructions { get; set; }

                public InstrDoc this[uint i] => Instructions.Find(ins => ins.Index == i);
            }

            public class InstrDoc
            {
                [JsonProperty(PropertyName = "name")]
                public string Name { get; set;  }

                [JsonProperty(PropertyName = "index")]
                public long Index { get; set; }

                [JsonProperty(PropertyName = "args")]
                public ArgDoc[] Arguments { get; set; }

                public ArgDoc this[uint i] => Arguments[i];
            }

            public class ArgDoc
            {
                [JsonProperty(PropertyName = "name")]
                public string Name { get; set; }

                [JsonProperty(PropertyName = "type")]
                public long Type { get; set; }

                [JsonProperty(PropertyName = "enum_name")]
                public string EnumName { get; set; }

                [JsonProperty(PropertyName = "default")]
                public long Default { get; set; }

                [JsonProperty(PropertyName = "min")]
                public long Min { get; set; }

                [JsonProperty(PropertyName = "max")]
                public long Max { get; set; }

                [JsonProperty(PropertyName = "increment")]
                public long Increment { get; set; }

                [JsonProperty(PropertyName = "format_string")]
                public string FormatString { get; set; }

                [JsonProperty(PropertyName = "unk1")]
                private long UNK1 { get; set; }

                [JsonProperty(PropertyName = "unk2")]
                private long UNK2 { get; set; }

                [JsonProperty(PropertyName = "unk3")]
                private long UNK3 { get; set; }

                [JsonProperty(PropertyName = "unk4")]
                private long UNK4 { get; set; }
            }

            public class EnumDoc
            {
                [JsonProperty(PropertyName = "name")]
                public string Name { get; set; }

                [JsonProperty(PropertyName = "values")]
                public Dictionary<string, string> Values { get; set; }
            }
        }

        #endregion
    }
}


