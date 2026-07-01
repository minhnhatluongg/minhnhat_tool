using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace minhnhat_tool.Services
{
    public class JobPublisher
    {
        // Thêm password vào job để worker login được. Dùng JsonSerializer cho an toàn
        // (mật khẩu có thể chứa ký tự đặc biệt " \ ...).
        public void PublishCrawlJob(string mst, string password, string tuNgay, string denNgay)
        {
            var factory = new ConnectionFactory { HostName = "localhost" };
            using var conn = factory.CreateConnection();
            using var channel = conn.CreateModel();

            channel.QueueDeclare(queue: "crawl_jobs", durable: true,
                                 exclusive: false, autoDelete: false, arguments: null);

            var job = new { mst, password, tuNgay, denNgay };
            string msg = JsonSerializer.Serialize(job);
            var bytes = Encoding.UTF8.GetBytes(msg);

            channel.BasicPublish(exchange: "", routingKey: "crawl_jobs",
                                 basicProperties: null, body: bytes);
        }
    }
}
