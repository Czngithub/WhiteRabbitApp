using System;
using System.Threading;
using SharpDX.DXGI;

namespace WhiteRabbit_Windows
{
    //限定该作用域内的属性如Device等属于SharpDX12
    using SharpDX.Direct3D12;
    using SharpDX.Windows;
    using SharpDX;
    using WhiteRabbitApp_Vertex;
    using WhiteRabbitApp_ConstantBuffer;

    //创建窗口
    public class Windows : IDisposable
    {
        //常量声明
        //帧数
        const int FrameCount = 2;

        //变量声明
        //裁剪矩形和视口
        private ViewportF viewPort;  //视口
        private Rectangle scissorRectangle;  //裁剪矩形

        //管道（渲染流水线）对象
        private Device device;       //设备
        private SwapChain3 swapChain;//交换链
        private readonly Resource[] renderTargets = new Resource[FrameCount];//渲染目标视图
        private CommandAllocator commandAllocator;  //命令分配器
        private CommandAllocator bundleAllocator;
        private CommandQueue commandQueue;  //命令队列
        private DescriptorHeap renderTargetViewHeap;    //描述符堆
        private DescriptorHeap constantBufferViewHeap;  //常量缓冲区描述符堆
        private GraphicsCommandList commandList;    //命令列表
        private GraphicsCommandList bundle;
        private PipelineState pipelineState;    //管道状态
        private int rtvDescriptorSize;  //管道
        private RootSignature rootSignature;    //根签名

        //同步对象
        private int width;
        private int height;
        private int frameIndex;
        private AutoResetEvent fenceEvent;  //帧同步事件
        private Fence fence;    //围栏
        private int fenceValue; //围栏描述符数值

        //App资源
        Resource vertexBuffer;  //顶点缓冲区
        VertexBufferView vertexBufferView;  //顶点缓冲区视图
        Resource constantBuffer;    //常量缓冲区
        ConstantBuffer.ConstantBuffer1 constantBufferData;  //常量缓冲区数据
        IntPtr constantBufferPointer;

        //初始化管道（渲染流水线）
        public void Initialize(RenderForm form)
        {
            LoadPipeline(form);
            LoadAssets();
        }

        //创建设备
        private void LoadPipeline(RenderForm form)
        {
            width = form.ClientSize.Width;
            height = form.ClientSize.Height;

            //创建视口
            viewPort.Width = width;
            viewPort.Height = height;
            viewPort.MaxDepth = 1.0f;

            //创建裁剪矩形
            scissorRectangle.Width = width;
            scissorRectangle.Bottom = height;

#if DEBUG
            //启用调试层
            {
                DebugInterface.Get().EnableDebugLayer();
            }
#endif
            device = new Device(null, SharpDX.Direct3D.FeatureLevel.Level_11_0);
            //工厂化
            using (var factory = new Factory4())
            {
                //描述并创建命令队列
                CommandQueueDescription queueDesc = new CommandQueueDescription(CommandListType.Direct);
                commandQueue = device.CreateCommandQueue(queueDesc);

                //描述交换链
                SwapChainDescription swapChainDesc = new SwapChainDescription()
                {
                    BufferCount = FrameCount,
                    ModeDescription = new ModeDescription(
                        width, height,          //缓存大小，一般与窗口大小相同
                        new Rational(60, 1),    //刷新率，60hz
                        Format.R8G8B8A8_UNorm), //像素格式，8位RGBA格式
                    Usage = Usage.RenderTargetOutput,   //CPU访问缓冲权限
                    SwapEffect = SwapEffect.FlipDiscard,//描述处理曲面后的缓冲区内容
                    OutputHandle = form.Handle,         //获取渲染窗口句柄
                    //Flags = SwapChainFlags.None,      //描述交换链的行为
                    SampleDescription = new SampleDescription(1, 0),    //一重采样
                    IsWindowed = true   //true为窗口显示，false为全屏显示
                };

                //创建交换链
                SwapChain tempSwapChain = new SwapChain(factory, commandQueue, swapChainDesc);
                swapChain = tempSwapChain.QueryInterface<SwapChain3>();
                tempSwapChain.Dispose();
                frameIndex = swapChain.CurrentBackBufferIndex;  //获取交换链的当前缓冲区的索引
            }

            //创建描述符堆
            //描述并创建一个渲染目标视图（RTV）的描述符堆
            DescriptorHeapDescription rtvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,   //堆中的描述符数
                Flags = DescriptorHeapFlags.None,   //结果值指定符堆，None表示堆的默认用法
                Type = DescriptorHeapType.RenderTargetView  //堆中的描述符类型
            };
            renderTargetViewHeap = device.CreateDescriptorHeap(rtvHeapDesc);

            //获取给定类型的描述符堆的句柄增量的大小，将句柄按正确的数量递增到描述符数组中
            rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            //描述并创建一个常量缓冲区视图（CBV）的描述符堆
            //待到绘制物体时，只要将CBV绑定到存有物体相应常量数据的缓冲区子区域即可
            var cbvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = 1,    //堆中的描述符数
                Flags = DescriptorHeapFlags.ShaderVisible,  //这里的参数指示该符堆被绑在命令列表上以供着色器参考
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView
            };
            constantBufferViewHeap = device.CreateDescriptorHeap(cbvHeapDesc);

            //创建渲染目标视图
            //获取堆中起始的CPU描述符句柄，for循环为交换链中的每一个缓冲区都创建了一个RTV(渲染目标视图)
            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            for (int n = 0; n < FrameCount; n++)
            {
                //获得交换链的第n个缓冲区
                renderTargets[n] = swapChain.GetBackBuffer<Resource>(n);
                device.CreateRenderTargetView(
                    renderTargets[n],   //指向渲染目标对象的指针
                    null,               //指向描述渲染目标视图结构的指针
                    rtvHandle);         //CPU描述符句柄，表示渲染目标视图的堆的开始
                rtvHandle += rtvDescriptorSize;
            }

            //创建命令分配器对象
            commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
            bundleAllocator = device.CreateCommandAllocator(CommandListType.Bundle);
        }

        //创建资源
        private void LoadAssets()
        {
            //创建一个空的根签名
            var rootSignatureDesc = new RootSignatureDescription(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                //根常量
                new[] {
                    new RootParameter(ShaderVisibility.Vertex,  //指定可以访问根签名绑定的内容的着色器，这里设置为顶点着色器
                    new DescriptorRange()
                    {
                        RangeType = DescriptorRangeType.ConstantBufferView, //指定描述符范围，这里的参数是CBV
                        BaseShaderRegister = 0, //指定描述符范围内的基本着色器
                        OffsetInDescriptorsFromTableStart = int.MinValue,   //描述符从根签名开始的偏移量
                        DescriptorCount = 1 //描述符范围内的描述符数
                    })
                }); 
            //表示该根签名需要一组顶点缓冲区来绑定
            rootSignature = device.CreateRootSignature(rootSignatureDesc.Serialize());

            //创建流水线状态，负责编译和加载着色器
#if DEBUG
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0"));
#endif

#if DEBUG
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0"));
#endif

            //描述输入装配器阶段的输入元素，这里定义顶点输入布局
            var inputElementDescs = new []
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 12, 0) 
            };

            //描述和创建流水线状态对象（PSO）
            var psoDesc = new GraphicsPipelineStateDescription()
            {
                InputLayout = new InputLayoutDescription(inputElementDescs),    //描述输入缓冲器
                RootSignature = rootSignature,  //根签名
                VertexShader = vertexShader,    //顶点着色器
                PixelShader = pixelShader,  //像素着色器
                RasterizerState = RasterizerStateDescription.Default(), //描述光栅器状态
                BlendState = BlendStateDescription.Default(),   //描述混合状态
                DepthStencilFormat = SharpDX.DXGI.Format.D32_Float, //描述深度/模板格式（纹理资源）
                DepthStencilState = new DepthStencilStateDescription()  //描述深度模板状态
                {
                    IsDepthEnabled = false, //不启用深度测试
                    IsStencilEnabled = false    //不启用模板测试
                },
                SampleMask = int.MaxValue,  //混合状态的样本掩码
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle, //定义该管道的几何或外壳着色器的输入类型，这里是三角
                RenderTargetCount = 1,  //RTVFormat成员中的渲染目标格式数
                Flags = PipelineStateFlags.None,    //用于控制管道状态的标志，这里表示没有标志
                SampleDescription = new SampleDescription(1, 0),   //描述资源的多采样参数
                StreamOutput = new StreamOutputDescription()    //描述输出缓冲器
            };
            psoDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm; //描述渲染目标格式的数组

            //设置管道
            pipelineState = device.CreateGraphicsPipelineState(psoDesc);

            //创建命令列表
            commandList = device.CreateCommandList(
                CommandListType.Direct, //指定命令列表的创建类型，Direct命令列表不会继承任何GPU状态
                commandAllocator,       //指向设备创建的命令列表对象的指针
                pipelineState);         //指向(管道)内存块的指针

            commandList.Close();

            //创建顶点缓冲区
            float aspectRatio = viewPort.Width / viewPort.Height;

            //定义三角形的几何形状
            var triangleVertices = new []
            {
                new Vertex.Vertex1() {Position = new Vector3(0.0f, 0.25f * aspectRatio, 0.0f),Color = new Vector4(0.0f, 0.0f, 0.0f, 1.0f) },
                new Vertex.Vertex1() {Position = new Vector3(0.25f, -0.25f * aspectRatio, 0.0f),Color = new Vector4(0.0f, 0.0f, 0.0f, 1.0f) },
                new Vertex.Vertex1() {Position = new Vector3(-0.25f, -0.25f * aspectRatio, 0.0f),Color = new Vector4(0.0f, 0.0f, 0.0f, 1.0f) },
            };

            //使用上传堆来传递垂直缓冲区的数据
            /*--------------------------------------------------*
             * 不推荐使用上传堆来传递像垂直缓冲区这样的静态数据 *
             * 这里使用上载堆是为了代码的简洁性，并且还因为需要 *
             * 传递的资源很少                                   *
             *--------------------------------------------------*/
            int vertexBufferSize = Utilities.SizeOf(triangleVertices);
            vertexBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(vertexBufferSize),
                ResourceStates.GenericRead);

            //将三角形的数据复制到顶点缓冲区
            IntPtr pVertexDataBegin = vertexBuffer.Map(0);
            Utilities.Write(
                pVertexDataBegin,
                triangleVertices,
                0,
                triangleVertices.Length);
            vertexBuffer.Unmap(0);

            //初始化顶点缓冲区视图
            vertexBufferView = new VertexBufferView();
            vertexBufferView.BufferLocation = vertexBuffer.GPUVirtualAddress;
            vertexBufferView.StrideInBytes = Utilities.SizeOf<Vertex.Vertex1>();
            vertexBufferView.SizeInBytes = vertexBufferSize;

            //创建bundle
            bundle = device.CreateCommandList(
                0, 
                CommandListType.Bundle, 
                bundleAllocator,
                pipelineState);
            bundle.SetGraphicsRootSignature(rootSignature);
            bundle.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            bundle.SetVertexBuffer(0, vertexBufferView);
            bundle.DrawInstanced(3, 1, 0, 0);
            bundle.Close();

            //使用上传堆来传递常量缓冲区的数据
            /*--------------------------------------------------*
             * 不推荐使用上传堆来传递像垂直缓冲区这样的静态数据 *
             * 这里使用上载堆是为了代码的简洁性，并且还因为需要 *
             * 传递的资源很少                                   *
             *--------------------------------------------------*/
            constantBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(1024 * 64),
                ResourceStates.GenericRead);

            //描述并创建常量缓冲区视图（CBV）
            var cbvDesc = new ConstantBufferViewDescription()
            {
                BufferLocation = constantBuffer.GPUVirtualAddress,
                SizeInBytes = (Utilities.SizeOf<ConstantBuffer.ConstantBuffer1>() + 255) & ~255
            };
            device.CreateConstantBufferView(
                cbvDesc, 
                constantBufferViewHeap.CPUDescriptorHandleForHeapStart);

            //初始化并映射常量缓冲区
            /*--------------------------------------------------*
             * 直到应用程序关闭，我们才会取消映射，因此在资源的 *
             * 生命周期中保持映射是可以的                       *
             *------------------------------------------------- */
            constantBufferPointer = constantBuffer.Map(0);
            Utilities.Write(constantBufferPointer, ref constantBufferData);

            //创建同步对象
            //创建围栏
            fence = device.CreateFence(
                0,                  //围栏的初始值
                FenceFlags.None);   //指定围栏的类型，None表示没有指定的类型
            fenceValue = 1;
            //创建用于帧同步的事件句柄
            fenceEvent = new AutoResetEvent(false);
        }

        //填充命令列表
        private void PopulateCommandList()
        {
            //命令列表分配器只有当相关的命令列表在GPU上执行完成后才能重置，应用应当使用围栏来确定GPU的执行进度
            commandAllocator.Reset();

            //但是当在特定的命令列表上调用ExecuteCommandList()时，可以随时重置该命令列表，并且必须在此之前重新写入
            commandList.Reset(commandAllocator, pipelineState);

            //设置描述符堆
            //更改与命令列表相关联的当前绑定的描述符堆
            commandList.SetDescriptorHeaps(
                1,  //要绑定的描述符堆的数量
                new DescriptorHeap[]
                {
                    constantBufferViewHeap
                }); //指向要在命令列表上设置的堆的对象的指针

            //设置根签名布局
            commandList.SetGraphicsRootSignature(rootSignature);

            //为描述符表设置图形根签名
            commandList.SetGraphicsRootDescriptorTable(
                0,  //用于绑定的符堆序号
                constantBufferViewHeap.GPUDescriptorHandleForHeapStart);    //用于设置基本描述符的GPU_descriptor_handle对象

            //设置视口和裁剪矩形
            commandList.SetViewport(viewPort);
            commandList.SetScissorRectangles(scissorRectangle);

            //命令返回缓冲区将用作渲染目标
            commandList.ResourceBarrierTransition(
                renderTargets[frameIndex],
                ResourceStates.Present,
                ResourceStates.RenderTarget);

            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIndex * rtvDescriptorSize;

            //为渲染目标和深度模板设置CPU描述符句柄
            commandList.SetRenderTargets(rtvHandle, null);

            //写入命令
            commandList.ClearRenderTargetView(rtvHandle, new Color4(1.0f, 1.0f, 1.0f, 1.0f), 0, null);

            //执行bundle
            commandList.ExecuteBundle(bundle);

            //使用返回缓冲区
            commandList.ResourceBarrierTransition(
                renderTargets[frameIndex],
                ResourceStates.RenderTarget,
                ResourceStates.Present);

            commandList.Close();
        }

        //等待前面的命令列表执行完毕
        private void WaitForPreviousFrame()
        {
            /*----------------------------------------------------------------*
             * 等待此帧的命令列表执行完毕，当前的实现没有什么效率，也过于简单 *
             * 将在后面重新组织渲染部分的代码，以免在每一帧都需要等待         *
             *----------------------------------------------------------------*/
            int fence = fenceValue;
            commandQueue.Signal(this.fence, fence);
            fenceValue++;

            //等待前面的帧结束
            if (this.fence.CompletedValue < fence)
            {
                this.fence.SetEventOnCompletion(
                    fence,
                    fenceEvent.SafeWaitHandle.DangerousGetHandle());
                fenceEvent.WaitOne();
            }

            frameIndex = swapChain.CurrentBackBufferIndex;
        }

        public void Update()
        {
            const float translationSpeed = 0.005f;
            const float offsetBounds = 1.25f;

            constantBufferData.Offset.X += translationSpeed;
            if (constantBufferData.Offset.X > offsetBounds)
            {
                constantBufferData.Offset.X = -offsetBounds;
            }
            Utilities.Write(constantBufferPointer, ref constantBufferData);
        }

        public void Render()
        {
            //将渲染场景所需的所有命令都记录到命令列表中
            PopulateCommandList();

            //执行命令列表
            commandQueue.ExecuteCommandList(commandList);

            //显示当前帧
            swapChain.Present(1, 0);

            //等待前一帧
            WaitForPreviousFrame();
        }

        //释放资源
        public void Dispose()
        {
            //等待GPU处理完所有的资源
            WaitForPreviousFrame();

            //释放所有资源
            foreach (var target in renderTargets)
            {
                target.Dispose();
            }
            commandAllocator.Dispose();
            bundleAllocator.Dispose();
            commandQueue.Dispose();
            rootSignature.Dispose();
            pipelineState.Dispose();
            vertexBuffer.Dispose();
            constantBuffer.Dispose();
            renderTargetViewHeap.Dispose();
            constantBufferViewHeap.Dispose();
            commandList.Dispose();
            bundle.Dispose();
            fence.Dispose();
            swapChain.Dispose();
            device.Dispose();
        }
    }
}


