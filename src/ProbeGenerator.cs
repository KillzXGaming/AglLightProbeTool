using AampLibraryCSharp;
using BfresLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AglLightProbeTool
{
    public class ProbeGenerator
    {
        private AampFile aamp;

        public ProbeGenerator(bool isSwitch)
        {
            aamp = new AampFile() {
                ParameterIOType = "glpbd",
                ParameterIOVersion =  0,
                RootNode = new ParamList() { HashString = "param_root" },
            };

            if (isSwitch)
                aamp = aamp.ConvertToVersion2();
            else
                aamp = aamp.ConvertToVersion1();
        }

        public void SetupProbes(ResFile resFile, Settings settings)
        {
            //here we just use the bounds of our whole bfres file
            var min_max = ResFileUtility.CalculateAABB(resFile);
            Vector3 step = new Vector3(1000f, 1000f, 1000f); //normally 100 step, but atm blanking probes so doesn't matter, keeps file size down

            SetupProbes(min_max.Item1, min_max.Item2, step, settings);
        }

        public void SetupProbes(Vector3 min, Vector3 max, Vector3 step, Settings settings)
        {
            List<ParamList> boxes = new List<ParamList>();
            //Just use root grid and one box atm
            boxes.Add(SetupProbeBox(min, max, step, settings));

            aamp.RootNode.paramObjects = new ParamObject[2];
            aamp.RootNode.paramObjects[0] = UpdateGridRoot(min, max, step);
            aamp.RootNode.paramObjects[1] = UpdateParameters((uint)boxes.Count, settings);

            aamp.RootNode.childParams = boxes.ToArray();
        }

        public ParamList SetupProbeBox(Vector3 min, Vector3 max, Vector3 step, Settings settings)
        {
            //Total number based on the grid size and step amount
            var num = CalculateIndexCount(min, max, step);

            ushort[] index_buffer = new ushort[num]; //number of indices to map in the grid to probes
            for (int i = 0; i < index_buffer.Length; i++)
                index_buffer[i] = 0;

            //Would index these per probe to figure out what to do
            List<float> color_buffer = new List<float>();
            //Keep things simple, use a constant color for now.
            color_buffer.AddRange(SHUtility.SetConstantColor(settings.Color));

            return SetupProbeParams(min, max, step, index_buffer, color_buffer.ToArray(), 0);
        }

        public ParamList SetupProbeParams(Vector3 min, Vector3 max, Vector3 step, ushort[] index_buffer_u16, float[] sh_buffer, int idx)
        {
            var packed_buffer = SetProbeIndicesUint32Buffer(index_buffer_u16);

            ParamList list = new ParamList() { HashString = $"b_{idx}", paramObjects = new ParamObject[4] };
            list.childParams = new ParamList[0];

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

        private ParamObject UpdateGridRoot(Vector3 min, Vector3 max, Vector3 step)
        {
            ParamObject obj = new ParamObject() { HashString = "root_grid" };
            obj.SetEntryValue("aabb_min_pos", min);
            obj.SetEntryValue("aabb_max_pos", max);
            obj.SetEntryValue("voxel_step_pos", step);
            return obj;
        }

        private ParamObject UpdateParameters(uint used_box_num, Settings settings)
        {
            ParamObject obj = new ParamObject() { HashString = "param_obj" };
            obj.SetEntryValue("version", 1u);
            obj.SetEntryValue("dir_light_indirect", settings.DirLightIndirect);
            obj.SetEntryValue("point_light_indirect", settings.PointLightIndirect);
            obj.SetEntryValue("spot_light_indirect", settings.SpotLightIndirect);
            obj.SetEntryValue("emission_scale", settings.EmissionScale);
            obj.SetEntryValue("used_box_num", used_box_num);
            return obj;
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

        public void Save(string filePath) {
            aamp.Save(filePath);

            Console.WriteLine($"Saved {filePath}");
        }

        public void SaveCompressed(string filePath) {
            var mem = new MemoryStream();
            aamp.Save(mem);
            File.WriteAllBytes(filePath, YAZ0.Compress(mem.ToArray()));

            Console.WriteLine($"Saved {filePath}");
        }

        public class Settings
        {
            public Vector3 Color { get; set; } = Vector3.One;
            public float DirLightIndirect { get; set; } = 0.525f;
            public float PointLightIndirect { get; set; } = 1f;
            public float SpotLightIndirect { get; set; } = 1f;
            public float EmissionScale { get; set; } = 8f;

            public static Settings Load(string folder)
            {
                string path = Path.Combine(folder, "settings.json");

                if (!File.Exists(path))
                    File.WriteAllText(path, JsonConvert.SerializeObject(new Settings(), Formatting.Indented));

                return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path));
            }
        }
    }
}
