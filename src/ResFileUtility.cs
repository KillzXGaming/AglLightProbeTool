using BfresLibrary.Helpers;
using BfresLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace AglLightProbeTool
{
    public class ResFileUtility
    {
        public static (Vector3, Vector3) CalculateAABB(ResFile resFile)
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
    }
}
