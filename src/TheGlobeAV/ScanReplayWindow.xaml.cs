using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using TheGlobeAV.Models;
using TheGlobeAV.Services;

namespace TheGlobeAV;

public partial class ScanReplayWindow : Window
{
    private readonly InspectionHistoryService _history = new();

    private readonly DispatcherTimer _timer;

    private InspectionRecord? _selectedRecord;

    private int _eventIndex;

    private double _playhead;

    private readonly Stopwatch _clock = new();

    private const double TimerIntervalMs = 50;

    public ScanReplayWindow(string initialQuery = "")
    {
        InitializeComponent();
        SearchBox.Text = initialQuery;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimerIntervalMs)
        };

        _timer.Tick += Playback_Tick;

        Loaded += async (_, _) => await SearchRecordsAsync();
        Closed += (_, _) => _timer.Stop();
    }

    private async Task SearchRecordsAsync()
    {
        PausePlayback();

        string query = SearchBox.Text.Trim();

        try
        {
            var records = await _history.SearchAsync(query);

            RecordsList.ItemsSource = records;

            SelectedFileText.Text =
                $"{records.Count} saved inspections found.";

            if (records.Count == 0)
                ReplayPositionText.Text =
                    "No matching inspection records.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Scan Replay Error");
        }
    }

    private async void Search_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SearchRecordsAsync();
    }

    private void RecordsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (RecordsList.SelectedItem is not InspectionRecord record)
            return;

        PausePlayback();

        _selectedRecord = record;

        RestartPlayback();

        SelectedFileText.Text =
            $"{record.FileName}\n" +
            $"{record.FilePath}\n" +
            $"SHA-256: {record.Sha256}\n" +
            $"Duration: {record.DurationMilliseconds} ms\n" +
            $"Recorded events: {record.Events.Count}";

        ReplayPositionText.Text =
            "Inspection loaded. Press PLAY to begin.";
    }

    private void Playback_Tick(
        object? sender,
        EventArgs e)
    {
        if (_selectedRecord == null)
        {
            PausePlayback();
            return;
        }

        double speed = SpeedSlider.Value;

        _playhead += TimerIntervalMs * speed;

        DisplayDueEvents();

        double finalTime = _selectedRecord.Events.Count == 0
            ? 0
            : _selectedRecord.Events.Max(
                item => item.OffsetMilliseconds);

        ReplayPositionText.Text =
            $"Playback: {_playhead:0} ms / {finalTime:0} ms";

        if (_eventIndex >= _selectedRecord.Events.Count)
        {
            PausePlayback();

            ReplayPositionText.Text =
                "Replay completed. All recorded events displayed.";
        }
    }

    private void DisplayDueEvents()
    {
        if (_selectedRecord == null)
            return;

        var events = _selectedRecord.Events;

        while (_eventIndex < events.Count)
        {
            var item = events[_eventIndex];

            if (item.OffsetMilliseconds > _playhead)
                break;

            AddEvent(item);
            _eventIndex++;
        }
    }

    private void AddEvent(InspectionEvent item)
    {
        ReplayEventsList.Items.Add(
            $"[{item.OffsetMilliseconds,6} ms] " +
            $"[{item.Category}] {item.Message}");

        ReplayEventsList.ScrollIntoView(
            ReplayEventsList.Items[
                ReplayEventsList.Items.Count - 1]);
    }

    private void Play_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedRecord == null)
        {
            MessageBox.Show(
                "Select a saved inspection first.",
                "Scan Replay");
            return;
        }

        if (_eventIndex >= _selectedRecord.Events.Count)
            RestartPlayback();

        _clock.Restart();
        _timer.Start();

        ReplayPositionText.Text = "Playing recorded inspection...";
    }

    private void PausePlayback()
    {
        _timer.Stop();
        _clock.Stop();
    }

    private void Pause_Click(
        object sender,
        RoutedEventArgs e)
    {
        PausePlayback();
        ReplayPositionText.Text = "Playback paused.";
    }

    private void Step_Click(
        object sender,
        RoutedEventArgs e)
    {
        PausePlayback();

        if (_selectedRecord == null)
            return;

        if (_eventIndex >= _selectedRecord.Events.Count)
            return;

        var item = _selectedRecord.Events[_eventIndex];

        _playhead = item.OffsetMilliseconds;

        AddEvent(item);

        _eventIndex++;

        ReplayPositionText.Text =
            $"Stepped to event {_eventIndex} / " +
            $"{_selectedRecord.Events.Count}";
    }

    private void RestartPlayback()
    {
        PausePlayback();

        _eventIndex = 0;
        _playhead = 0;

        ReplayEventsList.Items.Clear();

        ReplayPositionText.Text = "Replay reset.";

        if (_selectedRecord == null)
            return;

        ReplayPositionText.Text =
            $"Ready: {_selectedRecord.Events.Count} recorded events.";
    }

    private void Restart_Click(
        object sender,
        RoutedEventArgs e)
    {
        RestartPlayback();
    }

    private void SpeedSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedText != null)
            SpeedText.Text = $"{e.NewValue:0.0}x";
    }
}
