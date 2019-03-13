using SharpDX;

namespace WhiteRabbitApp_Vertex
{
    class Vertex
    {
        //创建顶点格式
        public struct Vertex1
        {
            public Vector4 Position;//位置信息（空间坐标），Vector4表示一个四位数学向量
            public Vector4 Color;   //颜色信息，Vector4表示一个四位数学向量
        }
    }
}
