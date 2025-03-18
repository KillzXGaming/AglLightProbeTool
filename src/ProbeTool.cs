using AampLibraryCSharp;
using BfresLibrary;
using BfresLibrary.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AglLightProbeTool
{
    public class ProbeTool
    {
        private AampFile aamp;

        public List<Box> Boxes = new List<Box>();

        //< 0xfff5 index = valid probe

        public const ushort INVISIBLE_PROBE_IDX = 0xfff5; //skip to use next box
        public const ushort EMPTY_PROBE_IDX = 0xfff6; //no probe in this spot
        public const ushort INITIAL_PROBE_IDX = 0xfff7;  //unsure what this does, state was mentioned in game code

        public void Load(string path)
        {
            Load(File.OpenRead(path));
        }

        public void Load(Stream stream)
        {
            aamp = AampFile.LoadFile(stream);

            foreach (var val in aamp.RootNode.childParams)
            {
                Box box = new Box();
                Boxes.Add(box);

                foreach (var param in val.paramObjects)
                {
                    if (param.HashString == "grid")
                    {
                        Vector3 min = param.GetEntryValue<Vector3>("aabb_min_pos");
                        Vector3 max = param.GetEntryValue<Vector3>("aabb_max_pos");
                        Vector3 step = param.GetEntryValue<Vector3>("voxel_step_pos");
                        var num = CalculateIndexCount(min, max, step);
                        Console.WriteLine();

                        box.Step = step;
                        box.Min = min;
                        box.Max = max;
                    }
                    if (param.HashString == "param_obj")
                    {

                    }
                    if (param.HashString == "sh_data_buffer")
                    {
                        int used_data_num = param.GetEntryValue<int>("used_data_num");
                        int per_probe_float_num = param.GetEntryValue<int>("per_probe_float_num");

                        float[] sh_buffer = (float[])param.GetEntry("data_buffer").Value;
                        box.Buffer = sh_buffer;
                    }
                    if (param.HashString == "sh_index_buffer")
                    {
                        int used_index_num = param.GetEntryValue<int>("used_index_num");
                        int max_index_num = param.GetEntryValue<int>("max_index_num");

                        uint[] index_buffer = (uint[])param.GetEntry("index_buffer").Value;
                        ushort[] probe_indices = GetProbeIndices(index_buffer);
                        box.ProbeIndices = probe_indices;
                    }
                }
            }
        }

        public void DumpImages()
        {
            string folder = $"Textures";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            for (int i = 0; i < Boxes.Count; i++)
            {
                Console.WriteLine($"Dumping {Path.Combine(folder, $"Box{i}")}");
                SHTexture.CreateImage(Boxes[i], Path.Combine(folder, $"Box{i}_"));
            }
        }

        public class Box
        {
            public ushort[] ProbeIndices;
            public float[] Buffer;

            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Step;
        }

        public void SetupProbes(ResFile resFile)
        {
            //here we just use the bounds of our whole bfres file
            var min_max = CalculateAABB(resFile);

            Vector3 min = min_max.Item1;
            Vector3 max = min_max.Item2;
            Vector3 step = new Vector3(1000f, 1000f, 1000f); //normally 100 step, but atm blanking probes so doesn't matter, keeps file size down

            List<ParamList> boxes = new List<ParamList>();
            //Just use root grid and one box atm
            boxes.Add(SetupProbeBox(min, max, step));

            aamp.RootNode.paramObjects = new ParamObject[2];
            aamp.RootNode.paramObjects[0] = UpdateGridRoot(min, max, step);
            aamp.RootNode.paramObjects[1] = UpdateParameters((uint)boxes.Count);

            aamp.RootNode.childParams = boxes.ToArray();
        }

        static (Vector3, Vector3) CalculateAABB(ResFile resFile)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;

            foreach (var model in resFile.Models.Values)
            {
                foreach (var shape in model.Shapes.Values)
                {
                    var vb = model.VertexBuffers[shape.VertexBufferIndex];
                    VertexBufferHelper helper = new VertexBufferHelper(vb, resFile.ByteOrder);
                    foreach (var pos in helper.Attributes[0].Data) //vertex pos _p0
                    {
                        minX = MathF.Min(pos.X, minX);
                        minY = MathF.Min(pos.Y, minY);
                        minZ = MathF.Min(pos.Z, minZ);
                        maxX = MathF.Max(pos.X, maxX);
                        maxY = MathF.Max(pos.Y, maxY);
                        maxZ = MathF.Max(pos.Z, maxZ);
                    }
                }
            }
            return (new Vector3(minX, minY, minZ), (new Vector3(maxX, maxY, maxZ)));
        }

        public ParamList SetupProbeBox(Vector3 min, Vector3 max, Vector3 step)
        {
            //Total number based on the grid size and step amount
            var num = CalculateIndexCount(min, max, step);

            ushort[] index_buffer = new ushort[num]; //number of indices to map in the grid to probes
            for (int i = 0; i < index_buffer.Length; i++)
                index_buffer[i] = 0;

            //Would index these per probe to figure out what to do
            List<float> color_buffer = new List<float>();

            float[] sh_data = new float[27];
            // SHUtil.UpdateCoeff(ref sh_data, new Vector3(1, 0, 0), new Vector3(-0.5f, -0.7f, 0.5f));

            color_buffer.AddRange(SHUtility.SetConstantColor(new Vector3(1)));
            color_buffer.AddRange(sh_data);

            var glsl_sh_data = SHUtility.ConvertSH2RGB(sh_data);
            var color = SHUtility.GetRGBColor(new Vector3(0, 1, 0), glsl_sh_data);

            return SetupProbes(min, max, step, index_buffer, color_buffer.ToArray(), 0);
        }


        static void ProbeStepExample(Vector3 min, Vector3 max, Vector3 step, ushort[] index_buffer, List<float[]> color_buffer)
        {
            ushort SetColorBufferIdx(float[] buffer)
            {
                if (!color_buffer.Contains(buffer)) //index the color to use
                    color_buffer.Add(buffer);

                return (ushort)color_buffer.IndexOf(buffer);
            }

            Vector3 size = max - min;
            Vector3 stride = size / step;
            stride.X = MathF.Ceiling(stride.X);
            stride.Y = MathF.Ceiling(stride.Y);
            stride.Z = MathF.Ceiling(stride.Z);

            int index = 0;
            //Step through the grid where probes are placed
            for (int x = 0; x < stride.X; x++)
            {
                for (int y = 0; y < stride.Y; y++)
                {
                    for (int z = 0; z < stride.Z; z++)
                    {
                        Vector3 pos = min + new Vector3(x, y, z);
                        //The game has 8 probes around the point for lighting in each corner
                        for (int p = 0; p < 8; p++)
                        {
                            var state = LightState.HasLight;
                            switch (state)
                            {
                                case LightState.HasLight:
                                    float[] sh_data = CalculateProbeColor(pos, p); //returns 27 sh data
                                    index_buffer[index] = SetColorBufferIdx(sh_data);
                                    break;
                                case LightState.CheckLightInOtherBox:
                                    index_buffer[index] = INVISIBLE_PROBE_IDX;
                                    break;
                                case LightState.NoLighting:
                                    index_buffer[index] = EMPTY_PROBE_IDX;
                                    break;
                            }
                            index++;
                        }
                    }
                }
            }
        }

        static float[] CalculateProbeColor(Vector3 pos, int probe_corner)
        {
            Vector3[] directions = new Vector3[1];
            directions[0] = new Vector3(0, -1, 0);

            Vector3[] colors = new Vector3[1];
            colors[0] = new Vector3(1, 0, 0);

            float[] sh_data = new float[27];
            SHUtility.UpdateCoeff(ref sh_data, colors[0], directions[0]);

            return sh_data;
        }

        enum LightState
        {
            HasLight,
            CheckLightInOtherBox,
            NoLighting,
        }

        public int CalculateIndexCount(Vector3 min, Vector3 max, Vector3 step)
        {
            //Get number of probes
            var size = max - min;
            var stride = size / step;
            stride.X = MathF.Ceiling(stride.X);
            stride.Y = MathF.Ceiling(stride.Y);
            stride.Z = MathF.Ceiling(stride.Z);
            return (int)(stride.X * stride.Y * stride.Z) * 8; //8 probes in each
        }

        public ParamList SetupProbes(Vector3 min, Vector3 max, Vector3 step, ushort[] index_buffer_u16, float[] sh_buffer, int idx)
        {
            var packed_buffer = SetProbeIndicesUint32Buffer(index_buffer_u16);

            ParamList list = new ParamList() { HashString = $"b_{idx}", paramObjects = new ParamObject[4] };
            {
                ParamObject obj = new ParamObject();
                obj.HashString = "param_obj";
                obj.SetEntryValue("index", idx);
                obj.SetEntryValue("type", 0); //always 0
                list.paramObjects[0] = obj;
            }
            {
                ParamObject obj = new ParamObject();
                obj.HashString = "grid";
                obj.SetEntryValue("aabb_min_pos", min);
                obj.SetEntryValue("aabb_max_pos", max);
                obj.SetEntryValue("voxel_step_pos", step);
                list.paramObjects[1] = obj;
            }
            {
                ParamObject obj = new ParamObject();
                obj.HashString = "sh_index_buffer";
                obj.SetEntryValue("type", 1); //index = 1, sh data = 0
                obj.SetEntryValue("used_index_num", packed_buffer.Length);
                obj.SetEntryValue("max_index_num", packed_buffer.Length);
                obj.SetEntryValue("index_buffer", packed_buffer);
                list.paramObjects[2] = obj;
            }
            {
                ParamObject obj = new ParamObject();
                obj.HashString = "sh_data_buffer";
                obj.SetEntryValue("type", 0); //index = 1, sh data = 0
                obj.SetEntryValue("max_sh_data_num", sh_buffer.Length / 27);
                obj.SetEntryValue("used_data_num", sh_buffer.Length / 27);
                obj.SetEntryValue("per_probe_float_num", 27);
                obj.SetEntryValue("data_buffer", sh_buffer);
                list.paramObjects[3] = obj;
            }

            return list;
        }

        public void Save(string path)
        {
            aamp.Save(path);
        }

        private ParamObject UpdateGridRoot(Vector3 min, Vector3 max, Vector3 step)
        {
            ParamObject obj = new ParamObject() { HashString = "root_grid" };
            obj.SetEntryValue("aabb_min_pos", min);
            obj.SetEntryValue("aabb_max_pos", max);
            obj.SetEntryValue("voxel_step_pos", step);
            return obj;
        }

        private ParamObject UpdateParameters(uint used_box_num)
        {
            ParamObject obj = new ParamObject() { HashString = "param_obj" };
            obj.SetEntryValue("version", 1u);
            obj.SetEntryValue("dir_light_indirect", 0.525f);
            obj.SetEntryValue("point_light_indirect", 1.0f);
            obj.SetEntryValue("spot_light_indirect", 1.0f);
            obj.SetEntryValue("emission_scale", 8.0f);
            obj.SetEntryValue("used_box_num", used_box_num);
            return obj;
        }

        public ushort[] GetProbeIndices(uint[] packedData)
        {
            ushort[] buffer = new ushort[packedData.Length * 2];
            for (int i = 0; i < packedData.Length; i++)
            {
                //Indices are ushorts packed into uints
                buffer[i] = (ushort)(packedData[i] >> 16);
                buffer[i + 1] = (ushort)(packedData[i] & 0xFFFF);
            }
            return buffer;
        }

        public uint[] SetProbeIndicesUint32Buffer(ushort[] unpackedData)
        {
            uint[] packedData = new uint[unpackedData.Length / 2];

            for (int i = 0; i < packedData.Length; i++)
            {
                uint lowUShort = unpackedData[i * 2];
                uint highUShort = (uint)unpackedData[i * 2 + 1] << 16;
                packedData[i] = lowUShort | highUShort;
            }

            return packedData;
        }
    }
}
