using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helrift.Gate.Contracts.Realm
{
    public enum RealmOperationType
    {
        Shutdown,        // kicks everyone at deadline
        MaintenanceMode, // deny new logins/joins while active
        Broadcast        // just message (no gating)
    }
}
