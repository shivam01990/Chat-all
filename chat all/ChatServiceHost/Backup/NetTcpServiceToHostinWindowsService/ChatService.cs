using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace NetTcpServiceToHostinWindowsService
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single,
                     ConcurrencyMode = ConcurrencyMode.Multiple,
                     UseSynchronizationContext = false)]
    public class ChatService : IChat, IDisposable
    {
       

        Dictionary<Client, IChatCallback> dictClientCallBacks = new Dictionary<Client, IChatCallback>();
        List<Client> clientsOnLine = new List<Client>();
        object syncObj = new object();

        public IChatCallback CurrentCallback
        {
            get
            {
                try
                {
                    return OperationContext.Current.GetCallbackChannel<IChatCallback>();
                }
                catch (Exception ex)
                {
                    //AppLog.ErrorFormat("{0}(): Failed to call GetCallbackChannel<IChatCallback>(). Exception: {1}",
                                       //new StackFrame().GetMethod().Name, ex);
                    return null;
                }
            }
        }

        private bool SearchClientsByName(string name)
        {
            Client foundClient = (from Client c in dictClientCallBacks.Keys
                                  where c.Name == name
                                  select c).FirstOrDefault();
            return foundClient != null;
        }

        public bool Connect(Client client)
        {
            string methodName = new StackFrame().GetMethod().Name;

            if (!dictClientCallBacks.ContainsValue(CurrentCallback) && !SearchClientsByName(client.Name))
            {
                return ProcessNewJoinConnection(client);
            }
            else if (SearchClientsByName(client.Name))
            {   // handle reenter to chat server        
                return ProcessReJoinConnection(client);
            }
            else
            {
                //AppLog.ErrorFormat("{0}(): Problem to connect to chat server. Facility: {1}, Client: {2}",
                                   //methodName, client.Facility, client.Name);
            }

            return false;
        }

        private bool ProcessNewJoinConnection(Client client)
        {
            string methodName = new StackFrame().GetMethod().Name;

            //AppLog.InfoFormat("{0}(): Chat client connected for client. facility: {1}, client: {2}",
                             // methodName, client.Facility, client.Name);

            lock (syncObj)
            {
                dictClientCallBacks.Add(client, CurrentCallback);
                clientsOnLine.Add(client);

                List<Client> clientsOnFacility = (from c in dictClientCallBacks.Keys
                                                  where c.Facility == client.Facility
                                                  select c).ToList();

                // call back to the new joined client first
                if (!CallbackUserJoined(clientsOnFacility, client, client))
                {
                    //AppLog.ErrorFormat("{0}(): Chat client failed to set up connection");
                    return false;
                }

                int clientNotified = 0;
                foreach (Client callBackClient in clientsOnFacility)
                {
                    if (client.Name != callBackClient.Name)
                    {
                        if (CallbackUserJoined(clientsOnFacility, client, callBackClient))
                        {
                            clientNotified++;
                        }
                    }
                }

                if (clientNotified > 0)
                {
                    //AppLog.ErrorFormat("{0}(): Failed to notify {1} client(s)", methodName, clientNotified);
                }
            }

            return true;
        }

        private bool CallbackUserJoined(List<Client> clientsOnFacility, Client client, Client callBackClient)
        {
            IChatCallback callback = dictClientCallBacks[callBackClient];
            try
            {
                callback.RefreshClients(clientsOnFacility);
                callback.UserJoin(client);
            }
            catch (Exception ex)
            {
                dictClientCallBacks.Remove(callBackClient);
                //AppLog.ErrorFormat("{0}(): Failed to callback. Facility: {1}, Client: {2}, Callback client: {3}, Exception {4}",
                                  // new StackFrame().GetMethod().Name, client.Facility, client.Name, callBackClient.Name, ex);
                return false;
            }

            return true;
        }

        private bool ProcessReJoinConnection(Client client)
        {
            string methodName = new StackFrame().GetMethod().Name;

            // notify everyone client rejoined
            lock (syncObj)
            {
                //AppLog.InfoFormat("{0}(): Chat client already exists. Facility: {1}, client: {2}",
                                 // methodName, client.Facility, client.Name);

                Client existingClient = (from c in dictClientCallBacks.Keys
                                         where c.Name == client.Name
                                         select c).FirstOrDefault();
                if (existingClient == null)
                {
                    //AppLog.ErrorFormat("{0}(): client does not exist in dictClientCallBacks. Client: {1}", methodName, client.Name);
                    return false;
                }

                if (dictClientCallBacks[existingClient] != CurrentCallback)
                {
                    dictClientCallBacks[client] = CurrentCallback;
                    //AppLog.InfoFormat("{0}(): Callback updated for client: {1}", methodName, client.Name);
                }
            }

            return true;
        }

        public void Say(ChatMessage msg)
        {
            lock (syncObj)
            {
                Client senderClient = (from c in dictClientCallBacks.Keys
                                       where c.Name == msg.Sender
                                       select c).FirstOrDefault();

                foreach (KeyValuePair<Client, IChatCallback> keyValuePair in dictClientCallBacks)
                {
                    Client client = keyValuePair.Key;
                    if (client.Facility == senderClient.Facility)
                    {
                        IChatCallback callBack = keyValuePair.Value;

                        try
                        {
                            callBack.Receive(msg);
                        }
                        catch (Exception ex)
                        {
                            //AppLog.ErrorFormat("{0}(): Failed to callback. Sender: {1}, Receiver: {2}, content: {3}, Exception: {4}",
                                              // new StackFrame().GetMethod().Name, msg.Sender, client.Name, msg.Content, ex);
                        }
                    }
                }
            }
        }

        public void Whisper(ChatMessage msg, Client receiver)
        {
            foreach (Client rec in dictClientCallBacks.Keys)
            {
                if (rec.Name == receiver.Name)
                {
                    try
                    {
                        IChatCallback callback = dictClientCallBacks[rec];
                        callback.ReceiveWhisper(msg, rec);

                        foreach (Client sender in dictClientCallBacks.Keys)
                        {
                            if (sender.Name == msg.Sender)
                            {
                                IChatCallback senderCallback = dictClientCallBacks[sender];
                                senderCallback.ReceiveWhisper(msg, rec);
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        //AppLog.ErrorFormat("{0}(): Failed to whisper. sender: {1}, Receiver: {2}, Exception: {3}.",
                                           //new StackFrame().GetMethod().Name, msg.Sender, receiver.Name, ex);
                    }
                }
            }
        }

        public bool SendFile(FileMessage fileMsg, Client receiver)
        {
            foreach (Client rcvr in dictClientCallBacks.Keys)
            {
                if (rcvr.Name == receiver.Name)
                {
                    try
                    {
                        string fileSizeText = string.Empty;
                        if (fileMsg.Data.Length < 1024)
                        {
                            fileSizeText = string.Format("{0} bytes", fileMsg.Data.Length);
                        }
                        else if ((fileMsg.Data.Length < 1024 * 1024))
                        {
                            fileSizeText = string.Format("{0:F2} KBytes", (float)fileMsg.Data.Length / 1024);
                        }
                        else
                        {
                            fileSizeText = string.Format("{0:F2} MBytes", (float)fileMsg.Data.Length / (1024 * 1024));
                        }

                        ChatMessage msg = new ChatMessage();
                        msg.Sender = fileMsg.Sender;
                        msg.Content = string.Format("I'm sending file. {0}, Size: {1}", fileMsg.FileName, fileSizeText);

                        IChatCallback rcvrCallback = dictClientCallBacks[rcvr];
                        rcvrCallback.ReceiveWhisper(msg, receiver);
                        rcvrCallback.ReceiverFile(fileMsg, receiver);

                        foreach (Client sender in dictClientCallBacks.Keys)
                        {
                            if (sender.Name == fileMsg.Sender)
                            {
                                IChatCallback sndrCallback = dictClientCallBacks[sender];
                                sndrCallback.ReceiveWhisper(msg, receiver);
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        //AppLog.ErrorFormat("{0}(): Failed to send file. Sender: {1}, FilePath: {2}, Receiver: {3}, Exception: {4}",
                                           //new StackFrame().GetMethod().Name, fileMsg.Sender, fileMsg.FileName, receiver.Name, ex);
                    }
                }
            }
            return false;
        }

        public void IsWriting(Client client)
        {
            ProcessIsWriting(client, false);
        }

        public void EndWriting(Client client)
        {
            ProcessIsWriting(client, true);
        }

        private void ProcessIsWriting(Client client, bool isEnding)
        {
            if (client == null)
            {
                //AppLog.ErrorFormat("{0}(): client is not specified", new StackFrame().GetMethod().Name);
                return;
            }

            foreach (KeyValuePair<Client, IChatCallback> keyValuePair in dictClientCallBacks)
            {
                Client callbackClient = keyValuePair.Key;
                if (callbackClient.Facility == client.Facility)
                {
                    IChatCallback callback = keyValuePair.Value;
                    try
                    {
                        if (isEnding)
                        {
                            callback.IsWritingCallback(null);
                        }
                        else
                        {
                            callback.IsWritingCallback(client);
                        }
                    }
                    catch (Exception ex)
                    {
                        //AppLog.ErrorFormat("{0}(): Failed to call IsWritingCallback. client: {1}, callBackClient: {2} Exception: {3}",
                                            //new StackFrame().GetMethod().Name, client.Name, callbackClient.Name, ex);
                    }
                }
            }
        }

        public void Disconnect(Client clientToRemove)
        {
            string methodName = new StackFrame().GetMethod().Name;
            //AppLog.InfoFormat("{0}(): Starting to disconnect from clients", methodName);

            lock (syncObj)
            {
                IList<Client> foundClients = (from c in dictClientCallBacks.Keys
                                              where c.Name == clientToRemove.Name
                                              select c).ToList();
                if (foundClients != null && foundClients.Count() > 0)
                {
                    foreach (Client eachFound in foundClients)
                    {
                        dictClientCallBacks.Remove(eachFound);
                        clientsOnLine.Remove(eachFound);
                    }
                }

                foreach (KeyValuePair<Client, IChatCallback> keyValuePair in dictClientCallBacks)
                {
                    Client client = keyValuePair.Key;
                    if (client.Facility == clientToRemove.Facility)
                    {
                        IChatCallback callBack = keyValuePair.Value;

                        try
                        {
                            callBack.RefreshClients(this.clientsOnLine);
                            callBack.UserLeave(clientToRemove);
                        }
                        catch (Exception ex)
                        {
                            //AppLog.ErrorFormat("{0}(): Failed to disconnect from client. client: {1}, Exception: {2}",
                                                //methodName, clientToRemove.Name, ex);
                        }
                    }
                }
            }

            return;
        }

        public List<Client> GetClientList()
        {
            return clientsOnLine;
        }

        public void Dispose()
        {
            //AppLog.InfoFormat("{0}(): Disposing chat service", new StackFrame().GetMethod().Name);
        }


    }
}
