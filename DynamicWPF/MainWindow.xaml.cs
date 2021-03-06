﻿using DynamicWPF.Scripting;
using DynamicWPF.Static;
using Microsoft.ClearScript.V8;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace DynamicWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        HttpClient client = new HttpClient();
        static readonly string[] allowedNamespaces = new[] { "System.Windows", "System.Windows.Controls", "System.Windows.Documents" };
        static Lazy<Type[]> availableTypesLazy = new Lazy<Type[]>(() => AppDomain.CurrentDomain.GetAssemblies()
                     .SelectMany(t => t.GetTypes())
                     .Where(t => !t.ContainsGenericParameters && (t.IsClass || t.IsSubclassOf(typeof(ValueType))) && allowedNamespaces.Contains(t.Namespace) && !t.FullName.Contains("+")).ToArray());

        public MainWindow()
        {
            Tools.SetBrowserEmulationMode();
            InitializeComponent();
        }

        V8ScriptEngine engine;

        private async Task Navigate(Uri uri)
        {
            controlsGrid.IsEnabled = false;
            try
            {
                string url = uri.ToString();
                urlBar.Text = url;

                if (engine != null)
                {
                    engine.Interrupt();
                    engine.Dispose();
                }

                engine = new V8ScriptEngine();
                engine.AddHostObject("window", this);
                engine.AddHostType(typeof(MessageBox));
                engine.AddHostType(typeof(Console));

                foreach (var t in availableTypesLazy.Value)
                {
                    engine.AddHostType(t);
                }

                ParserContext parserContext = new ParserContext();
                parserContext.XmlnsDictionary["s"] = "clr-namespace:DynamicWPF.Scripting;assembly=DynamicWPF";

                try
                {
                    var resp = await client.GetAsync(uri);

                    uri = resp.RequestMessage.RequestUri;
                    url = uri.ToString();
                    urlBar.Text = url;

                    parserContext.BaseUri = new Uri(url.Remove(url.LastIndexOf('/') + 1));

                    Page obj = null;

                    if (resp.IsSuccessStatusCode)
                    {
                        try
                        {
                            obj = (Page)XamlReader.Load(await resp.Content.ReadAsStreamAsync(), parserContext);
                        }
                        catch
                        {
                            Process.Start(url);
                        }
                    }
                    else
                    {
                        try
                        {
                            obj = (Page)XamlReader.Load(await resp.Content.ReadAsStreamAsync(), parserContext);
                        }
                        catch
                        {
                            resp.EnsureSuccessStatusCode();
                        }
                    }

                    if (obj != null)
                    {
                        contentFrame.Navigate(obj);
                        foreach (var link in (obj.Content as DependencyObject).FindVisualChildren<Hyperlink>())
                        {
                            link.RequestNavigate += async (o, ev) =>
                            {
                                ev.Handled = true;
                                var u = (o as Hyperlink).NavigateUri;

                                await Navigate(u.IsAbsoluteUri ? u : new Uri(parserContext.BaseUri, u));
                            };
                        }

                        engine.AddHostObject("content", obj);
                        foreach (var s in ScriptManager.GetScripts(obj))
                        {
                            try
                            {
                                var str = await client.GetStringAsync(s.Source.IsAbsoluteUri ? s.Source : new Uri(parserContext.BaseUri, s.Source));
                                engine.Execute(str);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    contentFrame.Navigate(new ErrorPage(ex));
                }
            }
            finally
            {
                controlsGrid.IsEnabled = true;
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await Navigate(new Uri(urlBar.Text));
        }

        private void backButton_Click(object sender, RoutedEventArgs e)
        {
            contentFrame.GoBack();
        }

        private void forwardButton_Click(object sender, RoutedEventArgs e)
        {
            contentFrame.GoForward();
        }
    }
}
