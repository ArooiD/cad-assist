using System.Windows;

namespace CadAssist.Kompas.Interop;

public partial class CompleteTaskDialog : Window
{
    public CompleteTaskDialog(ProjectTask task)
    {
        InitializeComponent();
        TaskTitleText.Text = $"{task.Id} — {task.Title}";
        CommentTextBox.Focus();
    }

    public string CommentText => CommentTextBox.Text.Trim();

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommentText))
        {
            ValidationText.Text = "Введите комментарий к завершению задачи.";
            CommentTextBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
