using AampLibraryCSharp;
using BfresLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AglLightProbeTool
{
    internal class UnityProbeGenerator
    {
        private AampFile aamp;

        public UnityProbeGenerator(bool isSwitch)
        {
            aamp = new AampFile()
            {
                ParameterIOType = "glpbd",
                ParameterIOVersion = 0,
                RootNode = new ParamList() { HashString = "param_root" },
            };

            if (isSwitch)
                aamp = aamp.ConvertToVersion2();
            else
                aamp = aamp.ConvertToVersion1();
        }

        public void Generate(string path)
        {
            UnityProbeBox box = new UnityProbeBox(path);
            SetupProbes(box, new Settings());

        }

        public void SetupProbes(UnityProbeBox box, Settings settings)
        {
            List<ParamList> boxes = new List<ParamList>();
            //Just use root grid and one box atm
            boxes.Add(SetupProbeBox(box, settings));

            aamp.RootNode.paramObjects = new ParamObject[2];
            aamp.RootNode.paramObjects[0] = UpdateGridRoot(box.Min, box.Max, box.Step);
            aamp.RootNode.paramObjects[1] = UpdateParameters((uint)boxes.Count, settings);

            aamp.RootNode.childParams = boxes.ToArray();
        }

        public ParamList SetupProbeBox(UnityProbeBox box, Settings settings)
        {
            // Padding
            box.Min = box.Min - box.Step * 0.5f;
            box.Max = box.Max + box.Step * 0.5f;

            //Total number based on the grid size and step amount
            var num = CalculateIndexCount(box.Min, box.Max, box.Step);

            ushort[] index_buffer = new ushort[num]; //number of indices to map in the grid to probes

            Vector3 size = box.Max - box.Min;
            Vector3 stride = size / box.Step;
            stride.X = MathF.Ceiling(stride.X);
            stride.Y = MathF.Ceiling(stride.Y);
            stride.Z = MathF.Ceiling(stride.Z);


            int CalculateIndex(int x, int y, int z) {
                return (int)(stride.X * stride.Z * y + stride.X * z + x);
            }

            int CalculateIndexUnity(int x, int y, int z) {
                return (int)(stride.X * stride.Z * y + stride.X * z + x);
            }

            List<float> color_buffer = new List<float>();
            Dictionary<float[], int> uniqueEntries = new Dictionary<float[], int>(new FloatArrayComparer());

            for (int y = 0; y < stride.Y; y++) {
                for (int z = 0; z < stride.Z; z++) {
                    for (int x = 0; x < stride.X; x++) {
                        for (int i = 0; i < 8; i++) {
                            // MK8 probe index. This is where it expects a probe to be in the grid
                            int index = CalculateIndex(x, y, z) * 8 + i;

                            // Get unity buffer 
                            int unityBufferIndex = CalculateIndexUnity((int)(stride.X - x), y, z) * 27;
                            var shData = box.Buffer.Skip(unityBufferIndex).Take(27).ToArray();

                            // Get index from the current buffer
                            if (!uniqueEntries.TryGetValue(shData, out int dataIndex))
                            {
                                dataIndex = color_buffer.Count;
                                uniqueEntries[shData] = dataIndex;
                                color_buffer.AddRange(shData);
                            }
                            // The index is / 27 as it indexes the set of sh data to use
                            index_buffer[index] = (ushort)(dataIndex / 27);
                        }
                    }
                }
            }

            return SetupProbeParams(box.Min, box.Max, box.Step, index_buffer, color_buffer.ToArray(), 0);
        }

        class FloatArrayComparer : IEqualityComparer<float[]>
        {
            public bool Equals(float[] a, float[] b)
            {
                if (a == null || b == null) return false;
                if (a.Length != b.Length) return false;
                return a.SequenceEqual(b);
            }

            public int GetHashCode(float[] obj)
            {
                unchecked
                {
                    int hash = 17;
                    foreach (float f in obj)
                    {
                        hash = hash * 23 + f.GetHashCode();
                    }
                    return hash;
                }
            }
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

        public void Save(string filePath)
        {
            aamp.Save(filePath);

            Console.WriteLine($"Saved {filePath}");
        }

        public void SaveCompressed(string filePath)
        {
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

        public class UnityProbeBox
        {
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Step = new Vector3(100);

            public float[] Buffer;

            public UnityProbeBox(string path)
            {
                using (var reader = new StreamReader(path))
                {
                    // Split the string using delimiters
                    string header = reader.ReadLine();
                    string[] parts = header.Split(';');

                    List<float> buffer = new List<float>();
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine().Replace(",", ".");
                        if (float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                            buffer.Add(v);
                    }

                    Buffer = buffer.ToArray();

                    if (parts.Length >= 3)
                    {
                        var minValues = ParseVector(parts[1]);
                        var maxValues = ParseVector(parts[2]);
                        Min = new Vector3(minValues.X, minValues.Y, minValues.Z);
                        Max = new Vector3(maxValues.X, maxValues.Y, maxValues.Z);

                        Console.WriteLine($"Min: {minValues}");
                        Console.WriteLine($"Max: {maxValues}");
                    }
                }
            }

            static (float X, float Y, float Z) ParseVector(string input)
            {
                input = input.Trim().Trim('(', ')');
                string[] values = input.Split(',');

                if (values.Length == 3 &&
                    float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    return (x, y, z);
                }
                throw new FormatException("Invalid vector format");
            }
        }
    }
}
