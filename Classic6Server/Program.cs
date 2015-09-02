using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Classic6;
using System.Windows.Forms;

namespace Classic6Server
{
    class Program
    {
        static ClassicServer server;

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting Minecraft Classic server on port 25565");
            server = new ClassicServer();
            server.MaxPlayers = 25;
            server.MessageOfTheDay = "Welcome to the Classic6 test server!";
            server.ServerName = "Classic6 Test Server";
            server.Start(25565);

            string url = server.ServerUrl.ToString();
            bool hasSet = false;

            while (true)
            {
                if (url != "" && !hasSet)
                {
                    Clipboard.SetText(server.ServerUrl.ToString());
                    hasSet = true;
                }
            }
        }
    }
}
