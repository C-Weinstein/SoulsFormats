using System;
using System.Collections.Generic;

namespace SoulsFormats.Formats
{
    public enum Game { DS1 = 0, BB = 1, DS3 = 2 }

    public enum BonfireHanlder { Normal = 0x00000000, Restart = 0x00000001, End = 0x00000002 }

    public class EMEVD : SoulsFile<EMEVD>
    {
        public Game Game;

        public int FileSize;
        public int EventCount;
        public int EventLayerCount;
        public int InstructionCount;
        public int ParamCount;
        public int LinkedFileCount;
        public int StringLength;
        public int ArgDataLength;

        public int InstructionTableOffset;
        public int EventTableOffset;
        public int EventLayerTableOffset;
        public int ParamTableOffset;
        public int LinkedFileOffset;
        public int StringOffset;
        public int ArgDataOffset;


        public List<Event> Events = new List<Event>();
        public List<Instruction> Instructions = new List<Instruction>();
        public List<Layer> Layers = new List<Layer>();
        public List<ulong> ArgData = new List<ulong>();
        public List<Parameter> Parameters = new List<Parameter>();
        public List<LinkedFile> LinkedFiles = new List<LinkedFile>();
        public List<ulong> Strings = new List<ulong>();

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

            /*
             * read different values depending on if the game targets 32-bit or 64-bit
             * and return them as int, convert them back to proper types on write
             */

            int uintW() => Game != Game.DS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();
            int sintW() => Game != Game.DS1 ? (int)br.ReadInt64() : br.ReadInt32();
            long zeroW() => Game != Game.DS1 ? br.AssertInt64(0) : br.AssertInt32(0);

            FileSize = uintW();
            EventCount = uintW();
            EventTableOffset = uintW();
            InstructionCount = uintW();
            InstructionTableOffset = uintW();

            zeroW();

            EventLayerTableOffset = uintW();
            EventLayerCount = uintW();
            if (uintW() != EventLayerTableOffset) throw new Exception("EventLayerTableOffset not consistent.");

            ParamCount = uintW();
            ParamTableOffset = uintW();
            LinkedFileCount = uintW();
            LinkedFileOffset = uintW();
            ArgDataLength = uintW();
            ArgDataOffset = uintW();
            StringLength = uintW();
            StringOffset = uintW();

            if (Game == Game.DS1) zeroW();
            #endregion

            Console.WriteLine("Reading events...");
            br.Position = EventTableOffset;
            for (int i = 0; i < EventCount; i++) Events.Add(new Event(br, Game));

            Console.WriteLine("Reading instructions...");
            br.Position = InstructionTableOffset;
            for (int i = 0; i < EventCount; i++) Instructions.Add(new Instruction(br, Game));

            Console.WriteLine("Reading layers...");
            br.Position = EventLayerTableOffset;
            for (int i = 0; i < EventLayerCount; i++) Layers.Add(new Layer(br, Game));

            Console.WriteLine("Reading argument data...");
            br.Position = ArgDataOffset;
            for (int i = 0; i < ArgDataLength; i++) ArgData.Add(br.ReadUInt64());
            if (Game == Game.DS1) br.AssertUInt32(0x00000000);

            Console.WriteLine("Reading parameters...");
            br.Position = ParamTableOffset;
            for (int i = 0; i < ParamCount; i++) Parameters.Add(new Parameter(br, Game));

            Console.WriteLine("Reading linked files...");
            br.Position = LinkedFileOffset;
            for (int i = 0; i < LinkedFileCount; i++) LinkedFiles.Add(new LinkedFile(br, Game));

            Console.WriteLine("Reading strings...");
            br.Position = StringOffset;
            while (br.Position < StringOffset + StringLength) Strings.Add(br.ReadUInt64());
        }

        internal override void Write(BinaryWriterEx bw)
        {

        }


        #region Subclasses

        public class Event
        {
            public Game Game;
            public BonfireHanlder BonfireHandler;

            public int ID;
            public int InstructionCount;
            public int InstructionOffset;
            public int ParameterCount;
            public int ParameterOffset;

            public Event(int id)
            {
                ID = id;
                InstructionCount = 0;
                ParameterCount = 0;
            }

            public Event(BinaryReaderEx br, Game g)
            {

                Game = g;

                int uintW() => Game != Game.DS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();
                int sintW() => Game != Game.DS1 ? (int)br.ReadInt64() : br.ReadInt32();

                ID = uintW();
                InstructionCount = uintW();
                InstructionOffset = uintW();
                ParameterCount = uintW();

                ParameterOffset = sintW();
                if (Game == Game.BB) br.AssertInt32(0);

                BonfireHandler = (BonfireHanlder)br.AssertInt32(0x00000000, 0x00000001, 0x00000002);

                br.AssertInt32(0);

            }


        }

        public class Instruction
        {
            Game Game;

            public uint InstructionClass;
            public uint InstructionIndex;
            public int ArgLength;
            public int ArgOffset;
            public int EventLayerOffset;

            public Instruction(BinaryReaderEx br, Game g)
            {
                Game = g;

                InstructionClass = br.ReadUInt32();
                InstructionIndex = br.ReadUInt32();
                ArgLength = Game != Game.DS1 ? (int)br.ReadUInt64() : (int)br.ReadUInt32();
                ArgOffset = Game != Game.DS1 ? (int)br.ReadInt64() : (int)br.ReadInt32();
                if (Game != Game.DS1) br.AssertInt32(0);

                EventLayerOffset = Game != Game.DS1 ? (int)br.ReadInt64() : (int)br.ReadInt32();
                if (Game != Game.DS3) br.AssertInt32(0);
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
