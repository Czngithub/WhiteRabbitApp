using SharpDX;

namespace WhiteRabbitApp_Vertex
{

    using SharpDX.Direct3D12;
    class Vertex
    {
        //创建顶点格式
        struct Vertex1
        {
            public Vector3 Position;//位置信息（空间坐标），Vector3表示一个三维数学向量
            public Vector4 Color;   //颜色信息，Vector4表示一个四位数学向量
        }
    }
}
