using System.Windows;
using TheGlobeAV.Services;

namespace TheGlobeAV;

public partial class DiscordSettingsWindow : Window
{
    private readonly DiscordWebhookService _discord = new();

    public DiscordSettingsWindow()
    {
        InitializeComponent();

        try
        {
            WebhookPassword.Password =
                _discord.LoadWebhookUrl() ?? "";

            StatusText.Text =
                WebhookPassword.Password.Length > 0
                    ? "An encrypted webhook is configured."
                    : "No webhook configured yet.";
        }
        catch
        {
            StatusText.Text =
                "Could not decrypt the saved webhook. Please configure it again.";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _discord.SaveWebhookUrl(WebhookPassword.Password);

            StatusText.Text =
                "Webhook encrypted and saved successfully.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Save failed: " + ex.Message;
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _discord.SaveWebhookUrl(WebhookPassword.Password);

            StatusText.Text = "Sending Discord test message...";

            await _discord.SendTestMessageAsync();

            StatusText.Text =
                "Discord connection successful. Test message sent.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Connection failed: " + ex.Message;
        }
    }
}
