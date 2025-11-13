using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts
{
    public class Envelope<T>
    {
        public string type { get; set; }
        public T payload { get; set; }

        public Envelope() { }

        public Envelope(string t, T p)
        {
            type = t;
            payload = p;
        }
    }
}
