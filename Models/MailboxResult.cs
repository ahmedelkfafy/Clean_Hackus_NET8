namespace Clean_Hackus_NET8.Models;

public class MailboxResult
{
    public string Address { get; set; }
    public string Password { get; set; }
    public string Request { get; set; }
    public int Count { get; set; }
    public Proxy Proxy { get; set; }

    public MailboxResult() { }

    public MailboxResult(Mailbox mailbox)
    {
        Address = mailbox.Address;
        Password = mailbox.Password;
    }

    public MailboxResult(string address, string password)
    {
        Address = address;
        Password = password;
    }

    public MailboxResult(Mailbox mailbox, string request, int count) : this(mailbox)
    {
        Request = request;
        Count = count;
    }

    public MailboxResult(string address, string password, string request, int count) : this(address, password)
    {
        Request = request;
        Count = count;
    }
}
