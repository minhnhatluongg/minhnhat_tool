using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace minhnhat_worker.Models
{
    public class CaptchaSolve
    {
        public string token { get; set; } = ""; // captcha đã giải
        public string key { get; set; } = "";   // mã phiên
    }
}
