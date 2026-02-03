using System;

public class ToDoItem
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string TaskDescription { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public override string ToString()
    {
        return $"{TaskDescription} ({Username}) ({Id})";
    }
}