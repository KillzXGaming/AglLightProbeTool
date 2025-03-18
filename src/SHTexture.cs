using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AglLightProbeTool
{
    public class SHTexture
    {
        public static void CreateImage(ProbeTool.Box box, string path)
        {
            Vector3 size = box.Max - box.Min;
            Vector3 stride = size / box.Step;
            stride.X = MathF.Ceiling(stride.X);
            stride.Y = MathF.Ceiling(stride.Y);
            stride.Z = MathF.Ceiling(stride.Z);

            ProbeDirImage[] images = new ProbeDirImage[8]; //for each direction
            for (int i = 0; i < images.Length; i++)
                images[i] = new ProbeDirImage()
                {
                    ImageData = new byte[((int)stride.X * (int)stride.Z * (int)stride.Y) * 4]
                };

            int CalculateIndex(int x, int y, int z)
            {
                return (int)(stride.X * stride.Z * y + stride.X * z + x);
            }

            for (int y = 0; y < stride.Y; y++)
            {
                for (int z = 0; z < stride.Z; z++)
                {
                    for (int x = 0; x < stride.X; x++)
                    {
                        for (int i = 0; i < images.Length; i++)
                        {
                            int index = CalculateIndex(x, y, z) * 8 + i;

                            var image = images[i].ImageData;
                            var probe_idx = box.ProbeIndices[index];

                            int pixelIndex = ((y * (int)stride.Z) + z) * (int)stride.X + x; // Corrected pixel index calculation
                            pixelIndex *= 4; // Move to the correct position in the byte array

                            switch (probe_idx)
                            {
                                case ProbeTool.INITIAL_PROBE_IDX:
                                case ProbeTool.INVISIBLE_PROBE_IDX:
                                case ProbeTool.EMPTY_PROBE_IDX:
                                    image[pixelIndex + 3] = 255;
                                    break;
                                default:
                                    var data_r = box.Buffer[probe_idx * 27 + 0];
                                    var data_g = box.Buffer[probe_idx * 27 + 9];
                                    var data_b = box.Buffer[probe_idx * 27 + 18];

                                    float[] sh = new float[27];
                                    for (int j = 0; j < 27; j++)
                                        sh[j] = box.Buffer[probe_idx * 27 + j];

                                    var c = new Vector3(sh[0], sh[9], sh[18]);
                                    var dir = new Vector3(sh[1], sh[10], sh[19]);
                                    var dir2 = new Vector3(sh[2], sh[11], sh[20]);
                                    var dir3 = new Vector3(sh[3], sh[12], sh[21]);
                                    var dir4 = new Vector3(sh[4], sh[13], sh[22]);
                                    var dir5 = new Vector3(sh[5], sh[14], sh[23]);

                                    var sh2rgb = SHUtility.ConvertSH2RGB(sh);
                                    var color = SHUtility.GetRGBColor(new Vector3(0, -1, 0), sh2rgb);

                                    image[pixelIndex + 0] = (byte)(color.X * 255);
                                    image[pixelIndex + 1] = (byte)(color.Y * 255);
                                    image[pixelIndex + 2] = (byte)(color.Z * 255);
                                    image[pixelIndex + 3] = 255;
                                    break;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < images.Length; i++)
            {
                var img = Image.LoadPixelData<Rgba32>(images[i].ImageData, (int)stride.X, (int)stride.Z * (int)stride.Y);
                img.SaveAsPng($"{path}{i}.png");
            }
        }

        class ProbeDirImage
        {
            public byte[] ImageData;
        }
    }
}
