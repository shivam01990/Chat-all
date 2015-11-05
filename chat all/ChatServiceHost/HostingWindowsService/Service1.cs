using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace HostingWindowsService
{
    public partial class Service1 : ServiceBase
    {
        public Service1()
        {
            InitializeComponent();
        }

        BackgroundWorker worker;
        internal static ServiceHost myHost = null;
        protected override void OnStart(string[] args)
        {
            worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(worker_DoWork);
            worker.RunWorkerAsync();
        }

        protected override void OnStop()
        {
            if (myHost != null)
            {
                myHost.Close();
                myHost = null;
            }
        }

        void worker_DoWork(object sender,DoWorkEventArgs e)
        {
            if (myHost != null)
            {
                myHost.Close();
            }

            myHost = new ServiceHost(typeof(NetTcpServiceToHostinWindowsService.ChatService));
            myHost.Open();
        }
    }
}
