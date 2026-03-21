// UseWindowsForms=true (TrayService の NotifyIcon 用) と WPF の名前空間が衝突するため、
// WPF 側を優先するグローバルエイリアスを定義する。
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using Orientation = System.Windows.Controls.Orientation;
