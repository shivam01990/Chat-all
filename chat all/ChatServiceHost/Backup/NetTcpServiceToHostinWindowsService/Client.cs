using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NetTcpServiceToHostinWindowsService
{

    [DataContract]
    public class Client
    {
        private string _name;
        private int _avatarID;
        private DateTime _time;
        private string _facility;

        [DataMember]
        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        [DataMember]
        public int AvatarID
        {
            get { return _avatarID; }
            set { _avatarID = value; }
        }

        [DataMember]
        public string Facility
        {
            get { return _facility; }
            set { _facility = value; }
        }

        [DataMember]
        public DateTime Time
        {
            get { return _time; }
            set { _time = value; }
        }
    }
}
