using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace NetTcpServiceToHostinWindowsService
{
    [ServiceContract(CallbackContract = typeof(IChatCallback),
                       SessionMode = SessionMode.Required)]
    public interface IChat
    {
        [OperationContract(IsInitiating = true)]
        bool Connect(Client client);

        [OperationContract(IsOneWay = true)]
        void Say(ChatMessage msg);

        [OperationContract(IsOneWay = true)]
        void Whisper(ChatMessage msg, Client receiver);

        [OperationContract(IsOneWay = true)]
        void IsWriting(Client client);

        [OperationContract(IsOneWay = true)]
        void EndWriting(Client client);

        [OperationContract(IsOneWay = false)]
        bool SendFile(FileMessage fileMsg, Client receiver);

        [OperationContract(IsOneWay = true, IsTerminating = true)]
        void Disconnect(Client client);

        [OperationContract(IsOneWay = false)]
        List<Client> GetClientList();
    }
}
