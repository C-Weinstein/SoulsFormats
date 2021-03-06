﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool IsBB => Game == Game.BB;
        private bool IsDS3 => Game == Game.DS3;
        private EMEDF Documentation;

        //for managing data when exporting

        internal List<Instruction> WriteInstructions = new List<Instruction>();
        internal List<Layer> WriteLayers = new List<Layer>();
        internal int ArgDataLength = 0;
        internal List<Parameter> WriteParameters = new List<Parameter>();
        internal List<LinkedFile> WriteLinkedFiles = new List<LinkedFile>();
        internal int StringDataLength = 0;

        internal long HeaderSize => IsDS1 ? 84 : 148;
        internal long EventSize => IsDS1 ? 28 : 48;
        internal long InstructionSize => IsDS1 ? 24 : 32;
        internal long LayerSize => IsDS1 ? 20 : 32;
        internal long ParameterSize => IsDS1 ? 20 : 32;
        internal long LinkedFileSize => IsDS1 ? 4 : 8;

        public struct FileInfo
        {
            public EMEVD File;
            public long EventCount;
            public long EventOffset;
            public long InstructionCount;
            public long InstructionOffset;
            public long LayerCount;
            public long LayerOffset;
            public long ParameterCount;
            public long ParameterOffset;
            public long LinkedFileCount;
            public long LinkedFileOffset;
            public long ArgDataLength;
            public long ArgDataOffset;
            public long StringDataLength;
            public long StringDataOffset;
        }


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

            if (IsDS1) Documentation = EMEDF.Read("ds1-common.emedf.json");
            else if (IsBB) Documentation = EMEDF.Read("bb-common.emedf.json");
            else if (IsDS3) Documentation = EMEDF.Read("ds3-common.emedf.json");
            
            long uintW() => !IsDS1 ? (long) br.ReadUInt64() : br.ReadUInt32();
            long zeroW() => !IsDS1 ? br.AssertInt64(0) : br.AssertInt32(0);

            FileInfo info = new FileInfo() { File = this};

            uintW();
            info.EventCount = uintW();
            info.EventOffset = uintW();
            info.InstructionCount = uintW();
            info.InstructionOffset = uintW();

            zeroW();

            info.LayerOffset = uintW();
            info.LayerCount = uintW();
            if (info.LayerOffset != uintW()) Debug.WriteLine("WARNING: Event layer table offset inconsistent.");

            info.ParameterCount = uintW();
            info.ParameterOffset = uintW();
            info.LinkedFileCount = uintW();
            info.LinkedFileOffset = uintW();
            info.ArgDataLength = uintW();
            info.ArgDataOffset = uintW();
            info.StringDataLength = uintW();
            info.StringDataOffset = uintW();

            if (IsDS1) zeroW();

            #endregion

            //read events, everything else added along the way

            br.Position = info.EventOffset;
            for (long i = 0; i < info.EventCount; i++)
                Events.Add(new Event(br, info));
        }

        internal override void Write(BinaryWriterEx bw)
        {
            ArgDataLength = 0;
            WriteInstructions.Clear();
            WriteLayers.Clear();
            WriteLinkedFiles.Clear();
            WriteParameters.Clear();
            StringDataLength = 0;

            bw.WriteASCII("EVD\0");
            if (IsDS1)
            {
                bw.WriteUInt32(0x00000000);
                bw.WriteUInt32(0x000000CC);
            } else if (IsBB)
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

            void fillUintW(string name, long? u = null)
            {
                if (!u.HasValue) u = bw.Position;
                if (IsDS1) bw.FillUInt32(name, (uint) bw.Position);
                else bw.FillUInt64(name, (ulong) bw.Position);
            }

            //write header
            resUintW("fileSize");
            uintW(Events.Count);
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
            resUintW("argDataLength"); //
            resUintW("argDataOffset");
            resUintW("stringDataLength");
            resUintW("stringDataOffset");

            //write events
            fillUintW("eventTableOffset");
            foreach (var evt in Events) evt.Write(bw);

            //write instructions
            fillUintW("instructionCount", WriteInstructions.Count);
            fillUintW("instructionTableOffset");
            foreach (var ins in WriteInstructions) ins.WriteIns(bw);

            //write layers
            fillUintW("layerCount", WriteLayers.Count);
            fillUintW("instructionTableOffset");
            foreach (var lay in WriteLayers) lay.Write(bw);

            //write argument data
            fillUintW("argDataLength", ArgDataLength + (IsDS1 ? 4 : 0));
            fillUintW("argDataOffset");
            foreach (var ins in WriteInstructions) ins.WriteArgs(bw);
            if (IsDS1) bw.WriteInt32(0);

            //write parameters
            fillUintW("paramCount", WriteParameters.Count);
            fillUintW("paramTableOffset");
            foreach (var par in WriteParameters) par.Write(bw);

            //write linked files
            fillUintW("linkedFileCount", WriteLinkedFiles.Count);
            fillUintW("linkedFileTableOffset");
            foreach (var lnk in WriteLinkedFiles) lnk.Write(bw);

            //write string data
            fillUintW("stringDataLength", StringDataLength);
            fillUintW("stringDataOffset");
            //bw.WriteBytes(WriteStringData.ToArray());

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

            public Event(long id, EMEVD emevd)
            {
                ID = id;
                File = emevd;
                BonfireHandler = BonfireHandler.Normal;
            }

            public Event(BinaryReaderEx br, FileInfo info)
            {

                File = info.File;

                long uintW() => !File.IsDS1 ? (long) br.ReadUInt64() : br.ReadUInt32();
                long sintW() => !File.IsDS1 ? br.ReadInt64() : br.ReadInt32();

                ID = uintW();

                long evtInsCount = uintW();
                long evtInsOffset = uintW();
                br.StepIn(info.InstructionOffset + evtInsOffset);
                for (int i = 0; i < evtInsCount; i++)
                    Instructions.Add(new Instruction(br, info));
                br.StepOut();

                long paramCount = uintW();
                long paramOffset = File.IsBB ? br.ReadUInt32() : sintW();
                br.StepIn(info.ParameterOffset + paramOffset);
                for (long i = 0; i < paramCount; i++)
                    Parameters.Add(new Parameter(br, info));
                br.StepOut();

                if (File.IsBB) br.AssertUInt32(0);
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
                    if (File.IsDS1) bw.WriteInt32(-1);
                    else if (File.IsBB)
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

            public Instruction(EMEVD emevd, uint insClass, uint insIndex, params dynamic[] args)
            {
                File = emevd;

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

            public Instruction(BinaryReaderEx br, FileInfo info)
            {
                File = info.File;

                InstructionClass = br.ReadUInt32();
                InstructionIndex = br.ReadUInt32();
                ArgDocs = File.Documentation[InstructionClass][InstructionIndex].Arguments;

                int insArgLength = File.IsDS1 ? (int)br.ReadUInt32() : (int)br.ReadUInt64();
                int insArgOffset = (int)br.ReadUInt32();
                if (!File.IsDS1) br.AssertInt32(0);

                br.StepIn(info.ArgDataOffset + insArgOffset);
                Arguments = ReadArgs(br);
                br.StepOut();

                long insLayOffset = File.IsDS3 ? br.ReadInt64() : br.ReadInt32();
                if (File.Game != Game.DS3) br.AssertInt32(0);
                if (insLayOffset != -1)
                {
                    br.StepIn(info.LayerOffset + insLayOffset);
                    EventLayer = new Layer(br, info);
                    br.StepOut();
                }
            }

            private dynamic[] ReadArgs(BinaryReaderEx br)
            {
                List<dynamic> args = new List<dynamic>();
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

            public void WriteIns(BinaryWriterEx bw)
            {
                bw.WriteUInt32(InstructionClass);
                bw.WriteUInt32(InstructionIndex);

                int argLength = 0;
                foreach (var doc in ArgDocs)
                {
                    if (doc.Type == 0 || doc.Type == 3) argLength++;
                    else if (doc.Type == 1 || doc.Type == 4)
                    {
                        while (argLength % 2 != 0) argLength++;
                        argLength += 2;
                    }
                    else
                    {
                        while (argLength % 4 != 0) argLength++;
                        argLength += 4;
                    }
                }
                bw.WriteInt32(argLength);
                if (!File.IsDS1) bw.WriteInt32(0);


                if (File.IsDS3)
                {
                    bw.WriteInt64(File.ArgDataLength);
                } else
                {
                    bw.WriteInt32(File.ArgDataLength);
                    bw.WriteInt32(0);
                }
                File.ArgDataLength += argLength;
            }

            public void WriteArgs(BinaryWriterEx bw)
            {
                for (int i = 0; i < ArgDocs.Length; i++)
                {
                    var doc = ArgDocs[i];
                    var arg = Arguments[i];

                    if (doc.Type == 0)
                    {
                        bw.WriteByte(arg);
                    }
                    else if (doc.Type == 1)
                    {
                        bw.Pad(2);
                        bw.WriteUInt16(arg);
                    }
                    else if (doc.Type == 2)
                    {
                        bw.Pad(4);
                        bw.WriteUInt32(arg);
                    }
                    else if (doc.Type == 3)
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

        public class Layer
        {
            public EMEVD File;

            public uint LayerNum;

            public Layer(BinaryReaderEx br, FileInfo info)
            {
                File = info.File;

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
                bw.WriteInt32(2);
                bw.WriteUInt32(LayerNum);
                if (File.IsDS1)
                {
                    bw.WriteUInt32(0);
                    bw.WriteInt32(-1);
                    bw.WriteUInt32(1);
                }
                else
                {
                    bw.WriteUInt64(0);
                    bw.WriteInt64(-1);
                    bw.WriteUInt64(1);
                }

            }
        }

        /* 
         * 
         * 
         * EVERYTHING BELOW HERE IS UNFINISHED
         * 
         * 
         */


        public class Parameter
        {
            public EMEVD File;

            public long InstructionNumber;
            public long DestinationStartByte;
            public long SourceStartByte;
            public long Length;

            public Parameter (BinaryReaderEx br, FileInfo info)
            {
                File = info.File;

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
            public EMEVD File;
            public string FileName;

            public LinkedFile (BinaryReaderEx br, FileInfo info)
            {
                File = info.File;
                long FileNameOffset = (long)(!File.IsDS1 ? br.ReadUInt64() : br.ReadUInt32());
                br.StepIn(info.StringDataOffset + FileNameOffset);
                FileName = br.ReadShiftJIS();
                br.StepOut();
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


