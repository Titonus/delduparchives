using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.IO;
using System.Windows.Controls;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace DelDupArch
{
    /// <summary>
    /// Lógica de interacción para MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();


            int nLogicalProcessors = Environment.ProcessorCount;

            do
            {
                ComboThreads.Items.Add(nLogicalProcessors);
            } while (nLogicalProcessors-- > 1);

            ComboThreads.SelectedItem = ComboThreads.Items[0];
        }

        CancellationTokenSource tokenSource = new CancellationTokenSource();

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select a folder";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FilesListBox.Items.Clear();
                    DirectoryTextBox.Text = dlg.SelectedPath;
                }
            }            
        }

        public class TrozosFicheroHASH
        {
            public long m_nTrozos { get; set; }
            public string m_strFichero { get; set;  }
            public long m_nOffsetBytes { get; set; }
            public long m_nSizeBytes { get; set; }
            public string m_hashCalculado { get; set; }
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            tokenSource = new CancellationTokenSource();

            CancelButton.IsEnabled = true;
            SelectFolder.IsEnabled = false;
            FindButton.IsEnabled = false;
            DirectoryTextBox.IsEnabled = false;
            ButtonDelete.IsEnabled = false;
            MaintainOriginal.IsEnabled = false;
            ProgressBar1.IsEnabled = true;

            FilesListBox.Items.Clear();

            string textoSuffix = PreSuffix.Text;
            Boolean blAdvice = false;
            Boolean blAdviceError = false;

            if (DirectoryTextBox.Text.Length > 0)
            {
                long nlBytesUp = 0;
                long nlBytesTot = 1;
                long nlBytes = 1;
             
                System.IO.DirectoryInfo dinfo = new DirectoryInfo(DirectoryTextBox.Text);
                List<string> lista = new List<string>();
                List<string> listaFinal = new List<string>();

                Boolean bMantener = (MaintainOriginal.IsChecked.HasValue && MaintainOriginal.IsChecked.Value == true);

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                ProgressBar1.Value = 0;

                ConcurrentBag<string> cb = new ConcurrentBag<string>();

                ConcurrentBag<string> cbDirs = new ConcurrentBag<string>();                

                TextProgress.Visibility = Visibility.Visible;

                TextProgress.Content = "Analyzing directory (recursive)...";

                FileProgress.Visibility = Visibility.Hidden;

                ConcurrentBag<FileInfo> listaFiles = new ConcurrentBag<FileInfo>();

                bool blAbort = false;

                var root = DirectoryTextBox.Text;

                CancellationToken token = tokenSource.Token;
                Boolean blcancelar = false;

                void GetAllDirectories(string strfDir, ref CancellationToken ftoken)
                {                   
                    try
                    {
                        if ( ftoken.IsCancellationRequested )
                        {
                            blcancelar = true;
                            return;
                        }

                        System.IO.DirectoryInfo dir_info = new DirectoryInfo(strfDir);

                        cbDirs.Add(dir_info.FullName);

                        var dir_info1 = dir_info.EnumerateDirectories("*.*",SearchOption.TopDirectoryOnly);

                        foreach ( var v in dir_info1)
                        {
                            if (ftoken.IsCancellationRequested)
                            {
                                blcancelar = true;
                                return;
                            }

                            GetAllDirectories(v.FullName, ref ftoken);
                        }
                    }
                    catch(Exception eeee)
                    {
                        //...
                    }
                }                

                void GetAllFiles(string strfDir, ref CancellationToken ftoken)
                {
                    try
                    {
                        System.IO.DirectoryInfo dir_info = new DirectoryInfo(strfDir);

                        var file_info1 = dir_info.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly);

                        if (ftoken.IsCancellationRequested)
                        {
                            blcancelar = true;
                            return;
                        }

                        Partitioner<FileInfo> partitioner = Partitioner.Create(file_info1, EnumerablePartitionerOptions.NoBuffering);
                        Parallel.ForEach(partitioner, f =>
                        {
                            listaFiles.Add(f);
                        });

                    }
                    catch (Exception eeee)
                    {
                        //...
                    }
                }

                await Task.Run(() =>
                {
                    try
                    {
                        GetAllDirectories(root, ref token);
                    }
                    catch (Exception eer)
                    {
                        System.Windows.Forms.MessageBox.Show("Error analyzing directories... (Try: Run as administrator)\n" + eer.Message, "Find Duplicates", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        blAbort = true;
                    }
                });

  
                if ( blcancelar )
                {
                    TextProgress.Content = "Cancelled";
                    ProgressBar1.Value = 100;
                    SelectFolder.IsEnabled = true;
                    FindButton.IsEnabled = true;
                    DirectoryTextBox.IsEnabled = true;
                    MaintainOriginal.IsEnabled = true;
                    ProgressBar1.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    return;
                }
                

                long nlDirectories = cbDirs.Count;
                long nlDirsWorked = 0;

                ProgressBar1.Value = 1;

                var progressEnumerateFiles = new Progress<long>(value => { nlDirsWorked += value; ProgressBar1.Value = ((nlDirsWorked * 8) / nlDirectories); }); //10% complete before hashing files

                await Task.Run(() =>
                {
                    try
                    {
                        Partitioner<string> partitioner = Partitioner.Create(cbDirs, EnumerablePartitionerOptions.NoBuffering);
                        Parallel.ForEach(partitioner, f =>
                        {
                            GetAllFiles(f, ref token);
                            ((IProgress<long>)progressEnumerateFiles).Report(1);
                        });

                    }
                    catch (Exception eer)
                    {
                        System.Windows.Forms.MessageBox.Show("Error analyzing directories... (Try: Run as administrator)\n" + eer.Message, "Find Duplicates", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        blAbort = true;
                    }

                });

                ProgressBar1.Value = 2;

                if ( blAbort )
                {
                    TextProgress.Visibility = Visibility.Hidden;
                    FileProgress.Visibility = Visibility.Hidden;
                    SelectFolder.IsEnabled = true;
                    FindButton.IsEnabled = true;
                    return;
                }

                if (blcancelar)
                {
                    TextProgress.Content = "Cancelled";
                    ProgressBar1.Value = 100;
                    SelectFolder.IsEnabled = true;
                    FindButton.IsEnabled = true;
                    DirectoryTextBox.IsEnabled = true;
                    MaintainOriginal.IsEnabled = true;
                    ProgressBar1.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    return;
                }

                TextProgress.Content = "Ordering "+listaFiles.Count+" files by creation time...";

                FileInfo[] listaFinalFiles = null;

                await Task.Run(() =>
                {
                    listaFinalFiles = listaFiles.OrderBy(p => p.CreationTime).ToArray();
                });        

                await Task.Run(() =>
                {
                    Partitioner<FileInfo> partitioner = Partitioner.Create(listaFinalFiles, true);
                    Parallel.ForEach(partitioner, f =>
                    {
                        cb.Add(f.FullName);
                        Interlocked.Add(ref nlBytes, f.Length);

                    });

                    lista = cb.ToList();

                });

                nlBytesTot = nlBytes;

                TextProgress.Visibility = Visibility.Hidden;

                int nlFiles = lista.Count;

                FileProgress.Content = nlBytes + " bytes remaining...";
                FileProgress.Visibility = Visibility.Visible;

                ProgressBar1.Value = 10;

                var progress = new Progress<long>(value => { nlBytesUp += value; ProgressBar1.Value = ((nlBytesUp * 90) / nlBytesTot) + 10; }); //10% complete before hashing files

                var progressFiles = new Progress<long>(value => { nlBytes -= value;

                    if (nlBytes >= 1024 * 1024 * 1024)
                    {
                        FileProgress.Content = String.Format("{0:0.00} {1}", (double)( (double)nlBytes/(1024 * 1024 * 1024) ), "GB remaining...");
                    }
                    else
                    {
                        if (nlBytes >= 1024*1024)
                        {
                            FileProgress.Content = String.Format("{0:0.00} {1}", (double)((double)nlBytes / (1024 * 1024)), "MB remaining...");
                        }
                        else
                        {
                            if (nlBytes >= 1024)
                            {
                                FileProgress.Content = String.Format("{0:0.00} {1}", (double)nlBytes/(double)1024, "KB remaining...");
                            }
                            else
                            {
                                FileProgress.Content = String.Format("{0} {1}", nlBytes, "bytes remaining...");
                            }
                        }
                    }
                });

                //dependiendo del nº ficheros o tamaño de todos ellos, vamos a dividir; ponderamos tamaño (1024*1024 = 1 MByte)
                void sha256func(ref ConcurrentDictionary<string, List<string>> sha256fichs, int offset, int lonoffset, int limiteBarra)
                {
                    for (int ix = offset; ix < (offset + lonoffset) && ix<lista.Count; ix++)
                    {
                        string hash256Calculated = "";

                        try
                        {
                            FileStream file = new FileStream(lista[ix], FileMode.Open, FileAccess.Read, FileShare.Read );

                            if (file != null)
                            {

                                long nlSize = file.Seek(0, SeekOrigin.End);
                                file.Seek(0, SeekOrigin.Begin);

                                if (nlSize >= (1024 * 1024) * 100) //si es mayor que 100 MB, vamos a particionarlo en trozos de 100 MB
                                {
                                    file.Close();

                                    long resto = (nlSize % ((1024 * 1024) * 100));

                                    long nlTrozos = nlSize / ((1024 * 1024) * 100);

                                    if (resto > 0)
                                    {
                                        nlTrozos++;
                                    }

                                    TrozosFicheroHASH[] slTrozos = new TrozosFicheroHASH[nlTrozos];

                                    long nlOffset = 0;

                                    for (int nlI = 0; nlI < nlTrozos; nlI++)
                                    {
                                        slTrozos[nlI] = new TrozosFicheroHASH
                                        {
                                            m_nTrozos = nlTrozos,

                                            m_strFichero = lista[ix],

                                            m_hashCalculado = "",

                                            m_nOffsetBytes = nlOffset
                                        };

                                        if (nlI + 1 == nlTrozos)
                                        {
                                            slTrozos[nlI].m_nSizeBytes = (nlSize % ((1024 * 1024) * 100));
                                        }
                                        else
                                        {
                                            slTrozos[nlI].m_nSizeBytes = (1024 * 1024) * 100;
                                        }

                                        nlOffset += (1024 * 1024) * 100;
                                    }

                                    Partitioner<TrozosFicheroHASH> partitioner = Partitioner.Create(slTrozos, true);
                                    Parallel.ForEach(partitioner, f =>
                                    {
                                        if (token.IsCancellationRequested)
                                        {
                                            blcancelar = true;
                                            return;
                                        }

                                        FileStream nfile = new FileStream(f.m_strFichero, FileMode.Open, FileAccess.Read, FileShare.Read);

                                        nfile.Seek(f.m_nOffsetBytes, SeekOrigin.Begin);
                                        byte[] buffer = new byte[262144];

                                        var hash256 = SHA256.Create();

                                        while (f.m_nSizeBytes > 0)
                                        {
                                            int bytesRead = nfile.Read(buffer, 0,
                                                (int)Math.Min(buffer.Length, f.m_nSizeBytes));

                                            if (bytesRead > 0)
                                            {
                                                f.m_nSizeBytes -= bytesRead;
                                                if (f.m_nSizeBytes > 0)
                                                    hash256.TransformBlock(buffer, 0, bytesRead,
                                                        buffer, 0);
                                                else
                                                    hash256.TransformFinalBlock(buffer, 0, bytesRead);

                                                ((IProgress<long>)progressFiles).Report(bytesRead); //nlSize remaining! KB
                                                ((IProgress<long>)progress).Report(bytesRead);
                                            }
                                            else
                                                throw new InvalidOperationException();
                                        }

                                        byte[] bytes = hash256.Hash;

                                        StringBuilder builder = new StringBuilder();
                                        for (int i = 0; i < bytes.Length; i++)
                                        {
                                            builder.Append(bytes[i].ToString("x2"));
                                        }

                                        f.m_hashCalculado += builder.ToString();

                                        nfile.Close();
                                    });

                                    //tenemos una lista de hashes cuyos strings a HEXA vamos a concatenar en uno sólo!
                                    for (int nlI = 0; nlI < nlTrozos; nlI++)
                                    {
                                        hash256Calculated += slTrozos[nlI].m_hashCalculado;
                                    }
                                }
                                else
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        blcancelar = true;
                                        return;
                                    }

                                    using (SHA256 sha256Hash = SHA256.Create())
                                    {
                                        // ComputeHash - returns byte array  
                                        byte[] bytes = sha256Hash.ComputeHash(file);

                                        // Convert byte array to a string   
                                        StringBuilder builder = new StringBuilder();
                                        for (int i = 0; i < bytes.Length; i++)
                                        {
                                            builder.Append(bytes[i].ToString("x2"));
                                        }

                                        hash256Calculated = builder.ToString();
                                    }

                                    ((IProgress<long>)progressFiles).Report((long)nlSize); //nlSize remaining! KB          
                                    ((IProgress<long>)progress).Report((long)nlSize);

                                    file.Close();
                                }

                                //common!
                                if (sha256fichs.ContainsKey(hash256Calculated))
                                {
                                    List<string> valores;

                                    bool blExito = false;

                                    do
                                    {
                                        blExito = sha256fichs.TryGetValue(hash256Calculated, out valores);
                                    } while (!blExito);

                                    valores.Add(lista[ix]);

                                    sha256fichs[hash256Calculated] = valores;

                                }
                                else
                                {
                                    bool blExito = false;

                                    do
                                    {
                                        blExito = sha256fichs.TryAdd(hash256Calculated, new List<string>() { lista[ix] });

                                        if (token.IsCancellationRequested)
                                        {
                                            blcancelar = true;
                                            return;
                                        }
                                    } while (!blExito);
                                }
                            }
                            else
                            {
                                blAdvice = true;
                            }

                        }
                        catch (Exception fileEx)
                        {
                            blAdviceError = true;
                            //... continuar
                        }

                    }

                }

                int nLogicalProcessors = int.Parse( ComboThreads.SelectedItem.ToString() ); // Environment.ProcessorCount;
                int concurrencyLevel = nLogicalProcessors * 2;

                ConcurrentDictionary<string, List<string>> sha256yFicherosDup = new ConcurrentDictionary<string, List<string>>(concurrencyLevel, lista.Count);

                //cada hilo le damos como mínimo 250 ficheros
                while (nLogicalProcessors > 1)
                {
                    int nFichs = lista.Count / nLogicalProcessors;

                    if (nFichs >= 250)
                    {
                        break;
                    }
                    else
                    {
                        nLogicalProcessors--;
                    }
                }

                int nlFicheros = lista.Count;
                List<Task> tareas = new List<Task>();
                List<Tuple<int, int, int>> tuplaInt = new List<Tuple<int, int, int>>();
                int remanente = 0;
                int remanenteFichs = 0;

                for (int ip = 0; ip < nLogicalProcessors; ip++)
                {
                    if (ip == nLogicalProcessors - 1)
                    {
                        remanente = 80 % nLogicalProcessors;
                        remanenteFichs = lista.Count % nLogicalProcessors;
                    };

                    tuplaInt.Add(new Tuple<int, int, int>((lista.Count / nLogicalProcessors) * ip, (lista.Count / nLogicalProcessors) + remanenteFichs, (80 / nLogicalProcessors) + remanente));
                }

                for (int ip = 0; ip < tuplaInt.Count; ip++)
                {
                    int id = ip; //si usa int ip, se lia

                    tareas.Add(Task.Factory.StartNew(() => sha256func(ref sha256yFicherosDup, tuplaInt[id].Item1, tuplaInt[id].Item2, tuplaInt[id].Item3)));
                }

                for (int ip = 0; ip < tareas.Count; ip++)
                {
                    await tareas[ip];
                }


                FileProgress.Visibility = Visibility.Hidden;
                TextProgress.Visibility = Visibility.Visible;

                TextProgress.Content = "Finalizing...";

                await Task.Run(() =>
                {
                    int valorAnterior = 0;
                    int nlLimite = 10;

                    //foreach (var vals in sha256yFicherosDuplicados)
                    for (int ix = 0; ix < sha256yFicherosDup.Count; ix++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            blcancelar = true;
                            return;
                        }

                        var vals = sha256yFicherosDup.ElementAt(ix);

                        var listaVals = vals.Value;

                        if (listaVals.Count > 1)
                        {
                            int a = 0;

                            foreach (var ficheros in listaVals)
                            {
                                Boolean blAgregar = true;

                                if (textoSuffix.Length > 0)
                                {
                                    var i = ficheros.LastIndexOf(textoSuffix);

                                    if (i < 0)
                                    {
                                        blAgregar = false;
                                    }
                                }
                                else
                                {
                                    //el primero encontrado de hash no se agrega si no hay prefijo sufijo
                                    if (a == 0 && bMantener==true)
                                    {
                                        blAgregar = false;
                                    }

                                    a++;
                                }

                                if (blAgregar)
                                {
                                    FileStream fbytes = new FileStream(ficheros, FileMode.Open);

                                    fbytes.Seek(0, SeekOrigin.End);
                                    
                                    listaFinal.Add(ficheros);

                                    fbytes.Close();
                                }
                            }
                        }

                        int temp = (((ix-0) * nlLimite) / (sha256yFicherosDup.Count));

                        if (temp != valorAnterior)
                        {
                            valorAnterior = temp;
                            //((IProgress<int>)progress).Report(1);
                        }
                        
                    }

                });

                if (blcancelar)
                {
                    TextProgress.Content = "Cancelled";
                    ProgressBar1.Value = 100;
                    SelectFolder.IsEnabled = true;
                    FindButton.IsEnabled = true;
                    DirectoryTextBox.IsEnabled = true;
                    MaintainOriginal.IsEnabled = true;
                    ProgressBar1.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    return;
                }

                ProgressBar1.Value = 100;

                foreach ( var nomFich in listaFinal )
                {
                    FilesListBox.Items.Add(nomFich);
                }

                TextProgress.Content = "Completed";

                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;

                TimeLabel.Content = "Time: " + elapsed_time + " ms";
                FilesFound.Content = "Duplicated Files: " + FilesListBox.Items.Count;
            }

            if (FilesListBox.Items.Count > 0)
            {
                ButtonDelete.IsEnabled = true;
                ButtonAll.IsEnabled = true;

                if (blAdvice)
                {
                    System.Windows.Forms.MessageBox.Show("Some files cannot be analyzed", "Find Duplicates", MessageBoxButtons.OK);
                }

                if (blAdviceError)
                {
                    System.Windows.Forms.MessageBox.Show("All files cannot be analyzed because a previous error analyzing directories or files", "Find Duplicates", MessageBoxButtons.OK);
                }
            }
            else
            {
                ButtonDelete.IsEnabled = false;
                ButtonAll.IsEnabled = false;

                System.Windows.Forms.MessageBox.Show("None file found", "Find Duplicates", MessageBoxButtons.OK);
            }

            SelectFolder.IsEnabled = true;
            FindButton.IsEnabled = true;
            DirectoryTextBox.IsEnabled = true;
            MaintainOriginal.IsEnabled = true;
            ProgressBar1.IsEnabled = false;
            CancelButton.IsEnabled = false;
        }

        private void ButtonAll_Click(object sender, RoutedEventArgs e)
        {
            FilesListBox.SelectAll();
        }

        private void ButtonDelete_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListBox.SelectedItems.Count > 0)
            {                
                var res = System.Windows.Forms.MessageBox.Show("You're going to delete "+ FilesListBox.SelectedItems.Count + " file(s), are you sure?", "Delete Selected Files", MessageBoxButtons.YesNo);

                if ( res == System.Windows.Forms.DialogResult.Yes )
                {
                    ButtonDelete.IsEnabled = false;

                    foreach (string fil in FilesListBox.SelectedItems)
                    {
                        if ( File.Exists(fil))
                        {
                            File.Delete(fil);
                        }
                    }

                    System.Windows.Forms.MessageBox.Show(FilesListBox.SelectedItems.Count + " files deleted", "Delete Selected Files", MessageBoxButtons.OK);

                    FilesListBox.Items.Clear();
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("No files selected", "Delete Selected Files", MessageBoxButtons.OK);
            }
        }

        private void MaintainOriginal_Checked(object sender, RoutedEventArgs e)
        {
            if (MaintainOriginal.IsChecked.HasValue && MaintainOriginal.IsChecked.Value == true)
            {
                PreSuffix.Text = "";
            }
        }

        private void PreSuffix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if ( PreSuffix.Text.Length>0 )
            {
                if (MaintainOriginal.IsChecked.HasValue && MaintainOriginal.IsChecked.Value == true)
                {
                    MaintainOriginal.IsChecked = false;
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (tokenSource!=null)
            {
                tokenSource.Cancel();
            }
        }
    }
}
