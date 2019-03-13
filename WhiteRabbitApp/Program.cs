using System;
using SharpDX.Windows;
using WhiteRabbit_Windows;

namespace WhiteRabbit_Program
{
    static class Program
    {
        //程序入口
        /*--------------*
         * 暂时为单线程 *
         * -------------*/
        [STAThread]
        static void Main()
        {
            var form = new RenderForm("WhiteRabbit")
            {
                Width = 1280,
                Height = 800

            };
            //窗口显示
            form.Show();

            using (var app = new Windows())
            {
                app.Initialize(form);

                //为创建窗口开启循环
                using (var loop = new RenderLoop(form))
                {
                    while (loop.NextFrame())
                    {
                        app.Update();
                        app.Render();
                    }
                }
            }
        }
    }
}
