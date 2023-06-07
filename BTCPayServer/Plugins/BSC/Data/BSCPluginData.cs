using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.BSC.Data
{
    public class BSCPluginData
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }
}
