using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AglLightProbeTool
{
    /// <summary>
    /// Utility for handling SH data for 27 spherical harmonic data.
    /// </summary>
    public class SHUtility
    {
        public const int NUM_DATA = 27;
        public const int NUM_CHANNELS = 3;

        /// <summary>
        /// Gets a constant color from the given SH data.
        /// </summary>
        public static Vector3 GetConstantColor(float[] buffer) {
            return new Vector3(buffer[0], buffer[9], buffer[18]);
        }

        /// <summary>
        /// Sets a constant color to the given SH data.
        /// </summary>
        public static float[] SetConstantColor(Vector3 color)
        {
            float[] sh_data = new float[NUM_DATA];
            sh_data[0]  = color.X; // L00 coefficient for Red channel
            sh_data[9]  = color.Y; // L00 coefficient for Green channel
            sh_data[18] = color.Z; // L00 coefficient for Blue channel
            return sh_data;
        }

        /// <summary>
        /// Computes all 27 spherical harmonic values. TODO is this correct?
        /// </summary>
        public static void UpdateCoeff(ref float[] sh_data, Vector3 color, Vector3 dir)
        {
            Vector3[] coeffVec = new Vector3[9];

            //Note we do not use any bias as that is computed automatically before passed to glsl

            //Constant color term
            coeffVec[0] += color; // L00 

            coeffVec[1] += color * (dir.Y); //L1-1
            coeffVec[2] += color * (dir.Z); //L10
            coeffVec[3] += color * (dir.X); //L11

            coeffVec[4] += color * (dir.X * dir.Y); //L2-2
            coeffVec[5] += color * (dir.Y * dir.Z); //L2-1
            coeffVec[6] += color * (dir.Z * dir.X); //L20

            coeffVec[7] += color * (dir.Z * dir.Z); //L21
            coeffVec[6] += color * ((dir.X * dir.X - dir.Y * dir.Y)); //L22

            sh_data = CoeffToBuffer(coeffVec);
        }

        //Packs coeff vec3[9] data into a float[27]
        static float[] CoeffToBuffer(Vector3[] coeffVec)
        {
            return new float[27]
            {
                //L00, L1-1,  L10,  L11, L2-2, L2-1,  L20,  L21,  L22, // red channel
                coeffVec[0].X, coeffVec[1].X, coeffVec[2].X, coeffVec[3].X,
                coeffVec[4].X, coeffVec[5].X, coeffVec[6].X, coeffVec[7].X,
                coeffVec[8].X,

                //L00, L1-1,  L10,  L11, L2-2, L2-1,  L20,  L21,  L22, // green  channel
                coeffVec[0].Y, coeffVec[1].Y, coeffVec[2].Y, coeffVec[3].Y,
                coeffVec[4].Y, coeffVec[5].Y, coeffVec[6].Y, coeffVec[7].Y,
                coeffVec[8].Y,

                //L00, L1-1,  L10,  L11, L2-2, L2-1,  L20,  L21,  L22, // blue channel
                coeffVec[0].Z, coeffVec[1].Z, coeffVec[2].Z, coeffVec[3].Z,
                coeffVec[4].Z, coeffVec[5].Z, coeffVec[6].Z, coeffVec[7].Z,
                coeffVec[8].Z,
            };
        }

        //Based on glsl representation, turns sh data into an rgba color
        public static Vector3 GetRGBColor(Vector3 normal, Vector4[] shdata)
        {
            Vector4 normal4 = new Vector4(normal, 1.0f);

            // x0 computation
            Vector3 x0 = new Vector3
            {
                X = Vector4.Dot(shdata[0], normal4),
                Y = Vector4.Dot(shdata[1], normal4),
                Z = Vector4.Dot(shdata[2], normal4)
            };

            // v_b and x1 computation
            Vector4 v_b = new Vector4(normal4.X * normal4.Y,
                                      normal4.Y * normal4.Z,
                                      normal4.Z * normal4.X,
                                      normal4.Z * normal4.Z);
            Vector3 x1 = new Vector3
            {
                X = Vector4.Dot(shdata[3], v_b),
                Y = Vector4.Dot(shdata[4], v_b),
                Z = Vector4.Dot(shdata[5], v_b)
            };

            // v_c and x2 computation
            float v_c = normal4.X * normal4.X - normal4.Y * normal4.Y;
            Vector3 x2 = new Vector3(shdata[6].X, shdata[6].Y, shdata[6].Z) * v_c;

            // Combine x0, x1, x2 and ensure non-negative result
            Vector3 result = Vector3.Max(x0 + x1 + x2, Vector3.Zero);

            return result;
        }

        /// <summary>
        /// Converts SH2 (3 channels, 9 values) into expected normalized rgb output used for glsl shaders.
        /// </summary>
        /// <param name="shData"></param>
        /// <returns></returns>
        public static Vector4[] ConvertSH2RGB(float[] shData)
        {
            float[] weights = new float[3] { 1, 1, 1 };
            int dataIndex = 0;

            Vector4[] rgb = new Vector4[7];
            for (int channel = 0; channel < 3; channel++)
            {
                //Convert 8 coefficents for each RGB channel (9th one will be done last)
                var data = ConvertChannel(weights[channel], new float[9] {
                        shData[dataIndex++], shData[dataIndex++],shData[dataIndex++],
                        shData[dataIndex++], shData[dataIndex++],shData[dataIndex++],
                        shData[dataIndex++], shData[dataIndex++],shData[dataIndex++]});
                //2 vec4 per channel
                rgb[channel] = data[0];
                //This channel goes after the first 3
                rgb[3 + channel] = data[1];
            }

            float const_5 = 0.1364044f;
            //Last value of each 9 coefficents convert for the last vec4
            rgb[6] = new Vector4(
                weights[0] * shData[8] * const_5,
                weights[1] * shData[19] * const_5,
                weights[2] * shData[26] * const_5,
                1.0f);

            return rgb;
        }

        static Vector4[] ConvertChannel(float weight, float[] data)
        {
            const float const_1 = 0.3253434f;
            const float const_2 = 0.2817569f;
            const float const_3 = 0.07875311f;

            const float const_4 = 0.2728088f;
            const float const_5 = 0.2362593f;

            float v1 = weight * data[3] * const_1;
            float v2 = weight * data[1] * const_1;
            float v3 = weight * data[2] * const_1;
            float v4 = weight * data[0] * const_2 - data[6] * const_3;

            float v21 = weight * data[4] * const_4;
            float v22 = weight * data[5] * const_4;
            float v23 = weight * data[6] * const_5;
            float v24 = weight * data[7] * const_4;

            return new Vector4[2]
            {
                new Vector4(v1, v2, v3, v4),
                new Vector4(v21, v22, v23, v24),
            };
        }
    }
}
