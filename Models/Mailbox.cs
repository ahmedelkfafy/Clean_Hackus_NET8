using System;

namespace Clean_Hackus_NET8.Models;

public class Mailbox
{
    public string Address { get; set; } = "";
    public string Password { get; set; } = "";
    public string Domain { get; set; } = "";

    public Mailbox() { }

    public Mailbox(string address, string password)
    {
        Address = address;
        Password = password;
    }

    public Mailbox(string address, string password, string domain) : this(address, password)
    {
        Domain = domain;
    }

    public static Mailbox? GetFromString(string value)
    {
        string[] array;
        if (value.Contains(":"))
        {
            array = value.Split(':');
        }
        else if (value.Contains(";"))
        {
            array = value.Split(';');
        }
        else
        {
            return null;
        }

        if (array.Length < 2 || string.IsNullOrWhiteSpace(array[0]) || string.IsNullOrWhiteSpace(array[1]))
        {
            return null;
        }

        string[] addressParts = array[0].Split('@');
        if (addressParts.Length < 2)
        {
            return null;
        }

        return new Mailbox(array[0], array[1], addressParts[1].ToLower());
    }

    public static Mailbox? Get(string address, string password)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        string[] addressParts = address.Split('@');
        if (addressParts.Length >= 2)
        {
            return new Mailbox(address, password, addressParts[1]);
        }
        return null;
    }
}
