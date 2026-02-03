using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfApp8
{
    public partial class MainWindow : Window
    {
        private readonly List<Connection> connections = new();
        private Canvas? draggingNode;
        private Ellipse? dragStartPort = null;
        private Line? tempDragLine = null;
        private Canvas? selectedNode = null;
        private Connection? selectedConnection = null;
        private Point lastMousePos;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Node drag
        private void Node_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Ellipse) return; // don't drag when clicking port

            // Select this node
            SelectNode((Canvas)sender);
            
            draggingNode = (Canvas)sender;
            lastMousePos = e.GetPosition(EditorCanvas);
            draggingNode.CaptureMouse();
            e.Handled = true;
        }

        private void SelectNode(Canvas node)
        {
            Debug.WriteLine($"SelectNode called for: {node.Name}");
            
            // Deselect previous node
            if (selectedNode != null && selectedNode != node)
            {
                // Remove selection highlight from previous node
                if (selectedNode.Children[0] is Border border)
                {
                    border.BorderBrush = Brushes.Black;
                    border.BorderThickness = new Thickness(2);
                }
            }

            // Deselect connection when selecting node
            DeselectConnection();

            selectedNode = node;
            
            // Add selection highlight
            if (node.Children[0] is Border selectedBorder)
            {
                selectedBorder.BorderBrush = Brushes.Blue;
                selectedBorder.BorderThickness = new Thickness(3);
                Debug.WriteLine("Node selection highlight applied");
            }
        }

        private void DeselectNode()
        {
            if (selectedNode != null)
            {
                // Remove selection highlight
                if (selectedNode.Children[0] is Border border)
                {
                    border.BorderBrush = Brushes.Black;
                    border.BorderThickness = new Thickness(2);
                }
                selectedNode = null;
            }
        }

        private void SelectConnection(Connection connection)
        {
            // Deselect previous connection
            DeselectConnection();
            
            // Deselect node when selecting connection
            DeselectNode();

            selectedConnection = connection;
            
            // Add selection highlight
            connection.VisualLine.Stroke = Brushes.Blue;
            connection.VisualLine.StrokeThickness = 4;
        }

        private void DeselectConnection()
        {
            if (selectedConnection != null)
            {
                // Remove selection highlight
                selectedConnection.VisualLine.Stroke = Brushes.Black;
                selectedConnection.VisualLine.StrokeThickness = 2.8;
                selectedConnection = null;
            }
        }

        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggingNode == null) return;

            var currentPos = e.GetPosition(EditorCanvas);
            var dx = currentPos.X - lastMousePos.X;
            var dy = currentPos.Y - lastMousePos.Y;

            Canvas.SetLeft(draggingNode, Canvas.GetLeft(draggingNode) + dx);
            Canvas.SetTop(draggingNode, Canvas.GetTop(draggingNode) + dy);

            lastMousePos = currentPos;
            UpdateConnectionsForNode(draggingNode);
        }

        private void Node_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (draggingNode != null)
            {
                draggingNode.ReleaseMouseCapture();
                draggingNode = null;
            }
        }

        // Port interaction
        private void Port_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Ellipse port) return;
            e.Handled = true;

            dragStartPort = port;
            ShowPortDot(port, true);

            var center = GetPortCenter(port);
            tempDragLine = new Line
            {
                X1 = center.X,
                Y1 = center.Y,
                X2 = center.X,
                Y2 = center.Y,
                Stroke = Brushes.DarkMagenta,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };

            // Add line to canvas but send it to back so it doesn't block hit testing
            EditorCanvas.Children.Add(tempDragLine);
            Panel.SetZIndex(tempDragLine, -1);
            EditorCanvas.MouseMove += Canvas_MouseMoveWhileDragging;
            EditorCanvas.MouseLeftButtonUp += Canvas_MouseUpWhileDragging;
        }

        private void Canvas_MouseMoveWhileDragging(object sender, MouseEventArgs e)
        {
            if (tempDragLine == null) return;
            var pos = e.GetPosition(EditorCanvas);
            tempDragLine.X2 = pos.X;
            tempDragLine.Y2 = pos.Y;
        }

        private void Canvas_MouseUpWhileDragging(object sender, MouseButtonEventArgs e)
        {
            EditorCanvas.MouseMove -= Canvas_MouseMoveWhileDragging;
            EditorCanvas.MouseLeftButtonUp -= Canvas_MouseUpWhileDragging;

            if (tempDragLine == null || dragStartPort == null)
            {
                Debug.WriteLine("MouseUp: No temp line or no start port ? early exit");
                return;
            }

            var dropPos = e.GetPosition(EditorCanvas);
            
            // Temporarily hide the drag line to prevent it from blocking hit testing
            if (tempDragLine != null)
                tempDragLine.Visibility = Visibility.Collapsed;
            
            var hitPort = FindPortAt(dropPos);
            
            // Show the line again
            if (tempDragLine != null)
                tempDragLine.Visibility = Visibility.Visible;

            Debug.WriteLine($"MouseUp at {dropPos}. Hit port? {(hitPort != null ? hitPort.Tag?.ToString() ?? "tagged but null" : "null")}");

            if (hitPort != null && hitPort != dragStartPort)
            {
                Debug.WriteLine($"VALID CONNECTION ATTEMPT: {dragStartPort.Tag} ? {hitPort.Tag}");

                var end = GetPortCenter(hitPort);
                tempDragLine.X2 = end.X;
                tempDragLine.Y2 = end.Y;
                tempDragLine.Stroke = Brushes.Black;
                tempDragLine.StrokeThickness = 2.8;
                tempDragLine.StrokeDashArray = null;
                tempDragLine.IsHitTestVisible = false;  // prevent future interference

                // Make damn sure it's still in the canvas
                if (!EditorCanvas.Children.Contains(tempDragLine))
                {
                    Debug.WriteLine("!!! Line was removed from canvas before finalize !!! Adding back.");
                    EditorCanvas.Children.Add(tempDragLine);
                }

                connections.Add(new Connection
                {
                    From = dragStartPort,
                    To = hitPort,
                    VisualLine = tempDragLine
                });

                ShowPortDot(hitPort, true);
                Debug.WriteLine($"Connection added. Total connections now: {connections.Count}. Line still in canvas? {EditorCanvas.Children.Contains(tempDragLine)}");

                // Optional: give it a small glow or tag so you can spot it in debugger
                tempDragLine.Tag = $"{dragStartPort.Tag}?{hitPort.Tag}";
            }
            else
            {
                Debug.WriteLine("Invalid drop (self or no port). Removing temp line.");
                if (EditorCanvas.Children.Contains(tempDragLine))
                {
                    EditorCanvas.Children.Remove(tempDragLine);
                }
            }

            // Clean up
            dragStartPort = null;
            tempDragLine = null;
        }

        private void Port_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Ellipse port)
                ShowPortDot(port, true);
        }

        private void Port_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Ellipse port)
            {
                // Only hide if not connected (you can improve this later)
                if (!connections.Any(c => c.From == port || c.To == port))
                    ShowPortDot(port, false);
            }
        }

        private void ShowPortDot(Ellipse port, bool visible)
        {
            string? dotName = port.Name switch
            {
                "PortIn" => "DotIn",
                "PortOut" => "DotOut",
                "PortErrIn" => "DotErrIn",
                "PortErrOut" => "DotErrOut",
                "PortErrErrOut" => "DotErrErrOut",
                _ => null
            };

            if (dotName == null) return;

            if (port.Parent is Grid grid && grid.FindName(dotName) is Ellipse dot)
            {
                dot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private Point GetPortCenter(Ellipse port)
        {
            return port.TranslatePoint(new Point(port.Width / 2, port.Height / 2), EditorCanvas);
        }

        private Ellipse? FindPortAt(Point position)
        {
            var hitResult = VisualTreeHelper.HitTest(EditorCanvas, position);
            if (hitResult?.VisualHit == null)
            {
                Debug.WriteLine($"HitTest returned null at {position}");
                return null;
            }

            DependencyObject? current = hitResult.VisualHit;
            Debug.WriteLine($"Initial hit: {current.GetType().Name} - {current}");

            while (current != null)
            {
                if (current is Ellipse ellipse)
                {
                    Debug.WriteLine($"Found Ellipse: {ellipse.Name}, Tag: {ellipse.Tag}, Fill: {ellipse.Fill}, StrokeThickness: {ellipse.StrokeThickness}");
                    
                    // Our port ellipses have Tag set to "In_xxx", "Out_xxx" etc.
                    if (ellipse.Tag?.ToString()?.Contains("_") == true)
                    {
                        Debug.WriteLine($"FOUND PORT: {ellipse.Tag} at {position}");
                        return ellipse;
                    }

                    // If it's one of the inner decorative ellipses (no Tag or wrong), keep going up
                    Debug.WriteLine($"Skipped inner ellipse (no valid Tag): {ellipse.Tag ?? "null"}");
                }

                current = VisualTreeHelper.GetParent(current);
            }

            Debug.WriteLine($"No valid port found after walking tree from {hitResult.VisualHit.GetType().Name}");
            return null;
        }

        private void UpdateConnectionsForNode(Canvas node)
        {
            foreach (var conn in connections)
            {
                if (IsPortChildOfNode(conn.From, node))
                {
                    var p = GetPortCenter(conn.From);
                    conn.VisualLine.X1 = p.X;
                    conn.VisualLine.Y1 = p.Y;
                }
                if (IsPortChildOfNode(conn.To, node))
                {
                    var p = GetPortCenter(conn.To);
                    conn.VisualLine.X2 = p.X;
                    conn.VisualLine.Y2 = p.Y;
                }
            }
        }

        private bool IsPortChildOfNode(Ellipse port, Canvas node)
        {
            DependencyObject? current = port;
            while (current != null)
            {
                if (current == node) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void EditorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check if we clicked on a connection line
            var clickPos = e.GetPosition(EditorCanvas);
            var hitResult = VisualTreeHelper.HitTest(EditorCanvas, clickPos);
            
            Debug.WriteLine($"Canvas clicked at: {clickPos}, HitResult: {hitResult?.VisualHit?.GetType().Name}");
            
            if (hitResult?.VisualHit is Line clickedLine)
            {
                Debug.WriteLine("Line clicked - finding connection");
                // Find the connection that matches this line
                var connection = connections.FirstOrDefault(c => c.VisualLine == clickedLine);
                if (connection != null)
                {
                    Debug.WriteLine("Connection found - selecting");
                    SelectConnection(connection);
                    e.Handled = true;
                    return;
                }
            }
            
            // Always unfocus text boxes unless we clicked on a RichTextBox
            if (hitResult?.VisualHit is not RichTextBox)
            {
                FocusManager.SetFocusedElement(this, null);
                Keyboard.ClearFocus();
            }
            
            // Check if we clicked on the actual canvas background (empty space)
            if (hitResult?.VisualHit == EditorCanvas)
            {
                Debug.WriteLine("Canvas background clicked - deselecting everything");
                DeselectNode();
                DeselectConnection();
                
                // Ensure the window maintains focus for keyboard events
                this.Focus();
                
                // Don't set e.Handled = true here to allow keybinds to work
                return;
            }
            
            // If we get here, we clicked on some UI element but not a connection line
            // Let the node handle its own selection if we clicked on it
            Debug.WriteLine($"Clicked on UI element: {hitResult?.VisualHit?.GetType().Name} - letting node handle selection");
        }

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Debug.WriteLine($"Key pressed: {e.Key}, Modifiers: {Keyboard.Modifiers}, Source: {e.OriginalSource}");
            
            // Handle deletion
            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                Debug.WriteLine("Delete/Backspace detected - calling DeleteSelectedItems");
                Debug.WriteLine($"Current selections - Node: {selectedNode?.Name}, Connection: {selectedConnection != null}");
                DeleteSelectedItems();
                e.Handled = true;
                return;
            }

            // Handle node creation with Ctrl
            if (Keyboard.Modifiers != ModifierKeys.Control) 
            {
                Debug.WriteLine($"No Ctrl modifier, ignoring key: {e.Key}");
                return;
            }
            
            var mousePos = Mouse.GetPosition(EditorCanvas);

            switch (e.Key)
            {
                case Key.N:
                    CreateGeneralNode(mousePos);
                    e.Handled = true;
                    break;
                case Key.E:
                    CreateErrorNode(mousePos);
                    CreateErrorReceivedNode(mousePos.X, mousePos.Y + 100);
                    e.Handled = true;
                    break;
                case Key.V:
                    CreateCombinatorNode(mousePos);
                    e.Handled = true;
                    break;
                case Key.S:
                    CreateSeparatorNode(mousePos);
                    e.Handled = true;
                    break;
                case Key.D1:
                    CreateStartNode(mousePos);
                    e.Handled = true;
                    break;
                case Key.D0:
                    CreateEndNode(mousePos);
                    e.Handled = true;
                    break;
            }
        }

        private void DeleteSelectedItems()
        {
            Debug.WriteLine($"DeleteSelectedItems called. SelectedNode: {selectedNode?.Name}, SelectedConnection: {selectedConnection}");
            
            // Delete selected node
            if (selectedNode != null)
            {
                Debug.WriteLine($"Deleting node: {selectedNode.Name}");
                
                // Remove all connections connected to this node
                var connectionsToRemove = connections.Where(c => 
                    IsPortChildOfNode(c.From, selectedNode) || IsPortChildOfNode(c.To, selectedNode)).ToList();
                
                Debug.WriteLine($"Found {connectionsToRemove.Count} connections to remove");
                
                foreach (var conn in connectionsToRemove)
                {
                    EditorCanvas.Children.Remove(conn.VisualLine);
                    connections.Remove(conn);
                }
                
                // Remove the node from canvas
                EditorCanvas.Children.Remove(selectedNode);
                selectedNode = null;
                
                Debug.WriteLine("Node deletion complete");
            }
            
            // Delete selected connection
            if (selectedConnection != null)
            {
                Debug.WriteLine("Deleting selected connection");
                EditorCanvas.Children.Remove(selectedConnection.VisualLine);
                connections.Remove(selectedConnection);
                selectedConnection = null;
                
                Debug.WriteLine("Connection deletion complete");
            }
        }

        private record Connection
        {
            public Ellipse From { get; init; } = null!;
            public Ellipse To { get; init; } = null!;
            public Line VisualLine { get; init; } = null!;
        }

        private void CreatePort(Canvas node, string type, double left, double top, string tag)
        {
            var portGrid = new Grid
            {
                Width = 24,
                Height = 24
            };
            Canvas.SetLeft(portGrid, left);
            Canvas.SetTop(portGrid, top);

            var outerEllipse = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.Transparent,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var dotEllipse = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var portEllipse = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                StrokeThickness = 0,
                Tag = tag
            };

            portEllipse.MouseLeftButtonDown += Port_MouseDown;
            portEllipse.MouseEnter += Port_MouseEnter;
            portEllipse.MouseLeave += Port_MouseLeave;

            portGrid.Children.Add(outerEllipse);
            portGrid.Children.Add(dotEllipse);
            portGrid.Children.Add(portEllipse);
            node.Children.Add(portGrid);
        }

        private void CreateErrorPort(Canvas node, double left, double top, string tag)
        {
            var portGrid = new Grid
            {
                Width = 24,
                Height = 24
            };
            Canvas.SetLeft(portGrid, left);
            Canvas.SetTop(portGrid, top);

            var outerEllipse = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromRgb(128, 0, 0)),
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var dotEllipse = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = Brushes.Red,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var portEllipse = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                StrokeThickness = 0,
                Tag = tag
            };

            portEllipse.MouseLeftButtonDown += Port_MouseDown;
            portEllipse.MouseEnter += Port_MouseEnter;
            portEllipse.MouseLeave += Port_MouseLeave;

            portGrid.Children.Add(outerEllipse);
            portGrid.Children.Add(dotEllipse);
            portGrid.Children.Add(portEllipse);
            node.Children.Add(portGrid);
        }

        private void AddCornerGradients(Canvas node)
        {
            var gradient1 = new Rectangle
            {
                Width = 2,
                Height = 2
            };
            Canvas.SetLeft(gradient1, 2);
            Canvas.SetTop(gradient1, 56);

            var brush1 = new LinearGradientBrush
            {
                EndPoint = new Point(0.5, 1),
                StartPoint = new Point(0.5, 0)
            };

            var transform1 = new TransformGroup();
            transform1.Children.Add(new ScaleTransform(0.5, 0.5, 0.5, 0.5));
            transform1.Children.Add(new SkewTransform(0, 0, 0.5, 0.5));
            transform1.Children.Add(new RotateTransform(135, 0.5, 0.5));
            transform1.Children.Add(new TranslateTransform());

            brush1.RelativeTransform = transform1;
            brush1.GradientStops.Add(new GradientStop(Colors.Gray, 0.5));
            brush1.GradientStops.Add(new GradientStop(Colors.White, 0.5));
            gradient1.Stroke = brush1;

            var gradient2 = new Rectangle
            {
                Width = 2,
                Height = 2
            };
            Canvas.SetLeft(gradient2, 96);
            Canvas.SetTop(gradient2, 2);

            var brush2 = new LinearGradientBrush
            {
                EndPoint = new Point(0.5, 1),
                StartPoint = new Point(0.5, 0)
            };

            var transform2 = new TransformGroup();
            transform2.Children.Add(new ScaleTransform(0.5, 0.5, 0.5, 0.5));
            transform2.Children.Add(new SkewTransform(0, 0, 0.5, 0.5));
            transform2.Children.Add(new RotateTransform(135, 0.5, 0.5));
            transform2.Children.Add(new TranslateTransform());

            brush2.RelativeTransform = transform2;
            brush2.GradientStops.Add(new GradientStop(Colors.Gray, 0.5));
            brush2.GradientStops.Add(new GradientStop(Colors.White, 0.5));
            gradient2.Stroke = brush2;

            node.Children.Add(gradient1);
            node.Children.Add(gradient2);
        }

        private void CreateGeneralNode(Point position)
        {
            var node = new Canvas
            {
                Name = $"NodeGeneral_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, position.X - 50);
            Canvas.SetTop(node, position.Y - 30);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(4),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("General"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "In", -12, 20, "In_General");
            CreatePort(node, "Out", 88, 20, "Out_General");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

        private void CreateErrorNode(Point position)
        {
            var node = new Canvas
            {
                Name = $"NodeError_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, position.X - 50);
            Canvas.SetTop(node, position.Y - 30);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 171, 173, 179)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0, 171, 173, 179)),
                SelectionBrush = new SolidColorBrush(Color.FromArgb(0, 171, 173, 179)),
                Margin = new Thickness(4),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("Error"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "In", -12, 20, "In_Error");
            CreatePort(node, "Out", 88, 20, "Out_Error");
            CreateErrorPort(node, 38, -16, "ErrorOut_Error");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

        private void CreateErrorReceivedNode(double x, double y)
        {
            var node = new Canvas
            {
                Name = $"NodeErrorReceived_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, x);
            Canvas.SetTop(node, y);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(4),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("Error Received"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "In", -12, 20, "In_ErrorReceived");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

        private void CreateCombinatorNode(Point position)
        {
            var node = new Canvas
            {
                Name = $"NodeCombinator_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, position.X - 50);
            Canvas.SetTop(node, position.Y - 30);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(4),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("Combinator"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "In", -12, 8, "In_Combinator_1");
            CreatePort(node, "In", -12, 32, "In_Combinator_2");
            CreatePort(node, "Out", 88, 20, "Out_Combinator");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

        private void CreateSeparatorNode(Point position)
        {
            var node = new Canvas
            {
                Name = $"NodeSeparator_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, position.X - 50);
            Canvas.SetTop(node, position.Y - 30);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(10, 10, 8, 8),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("Separator"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "In", -12, 20, "In_Separator");
            CreatePort(node, "Out", 88, 8, "Out_Separator_1");
            CreatePort(node, "Out", 88, 32, "Out_Separator_2");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

        private void CreateStartNode(Point position)
        {
            var node = new Canvas
            {
                Name = $"NodeStart_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, position.X - 50);
            Canvas.SetTop(node, position.Y - 30);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(10, 10, 8, 8),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("START"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "Out", 88, 20, "Out_Start");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

        private void Render_Click(object sender, RoutedEventArgs e)
        {
            // Replace "MyCanvas" with the actual name of your Canvas
            var canvas = EditorCanvas;

            // Measure and arrange the canvas
            Size size = new Size(canvas.ActualWidth, canvas.ActualHeight);
            canvas.Measure(size);
            canvas.Arrange(new Rect(size));

            // Render to bitmap
            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)size.Width,
                (int)size.Height,
                96d, 96d,
                PixelFormats.Pbgra32);

            rtb.Render(canvas);

            // Encode as PNG
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            // Save to file
            using (var fs = new FileStream("output.png", FileMode.Create))
            {
                encoder.Save(fs);
            }
        }


        private void CreateEndNode(Point position)
        {
            var node = new Canvas
            {
                Name = $"NodeEnd_{Guid.NewGuid():N}",
                Width = 100,
                Height = 60
            };
            Canvas.SetLeft(node, position.X - 50);
            Canvas.SetTop(node, position.Y - 30);

            var border = new Border
            {
                Width = 100,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(212, 208, 200)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(2)
            };

            var innerBorder1 = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2, 2, 0, 0)
            };

            var innerBorder2 = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 2, 2),
                Margin = new Thickness(-2, -2, 0, 0)
            };

            var richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(4),
                Width = 78,
                Height = 40
            };

            var doc = new FlowDocument();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("END"));
            doc.Blocks.Add(paragraph);
            richTextBox.Document = doc;

            innerBorder2.Child = richTextBox;
            innerBorder1.Child = innerBorder2;
            border.Child = innerBorder1;
            node.Children.Add(border);

            CreatePort(node, "In", -12, 20, "In_End");
            AddCornerGradients(node);

            node.MouseLeftButtonDown += Node_MouseDown;
            node.MouseMove += Node_MouseMove;
            node.MouseLeftButtonUp += Node_MouseUp;

            EditorCanvas.Children.Add(node);
        }

    }
}
