using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var factory = new ConnectionFactory { HostName = "localhost" };
using var conn = factory.CreateConnection();
using var channel = conn.CreateModel();

// Khai báo queue Y HỆT bên Producer (durable/exclusive/autoDelete phải khớp)

channel.QueueDeclare(queue: "crawl_jobs", durable: true, exclusive: false, autoDelete: false, arguments: null);

Console.WriteLine(" [*] Worker is waiting for jobs... (Ctrl + C to exit)");

var consumer = new EventingBasicConsumer(channel);
consumer.Received += (model, ea) =>
{
    var body = ea.Body.ToArray();
    var message = Encoding.UTF8.GetString(body);
    Console.WriteLine($" [x] Received job: {message}");
};

channel.BasicConsume(queue: "crawl_jobs", autoAck: true, consumer: consumer);

Console.ReadLine(); // Giữ console mở để nhận job

