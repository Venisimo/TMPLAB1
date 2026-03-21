using System.Text;
using TMPLAB1;

namespace ConsoleApp
{
    internal class Program
    {
        static void Help(string fileName)
        {
            string[] lines =
            {
                "Список команд:",
                "Create <имя файла> - создает файл с расширением prd",
                "Open <имя файла> - открывает указанный файл для работы с ним",
                "Input (имя компонента, тип) Input (имя компонента, тип) — включает компонент в список. тип — одно из следующего: Изделие, Узел, Деталь.",
                "Input (имя компонента, имя комплектующего) — включает комплектующее в спецификацию компонента.",
                "Delete <имя компонента> — помечает компонент на удаление",
                "Restore <имя компонента> - убирает пометку на уделение у указанного компонента",
                "Restore * - убирает пометку на уделение у всех компонентов",
                "Truncate - окончательно удалет компоненты помечанные на удаления",
                "Print <имя компонента> - вывод на экран состав компонента",
                "Print * - вывод всех компонентов",
                "Exit - закрыть все файлы и завершить программу"
            };

            string text = string.Join(Environment.NewLine, lines);

            if (fileName == null)
            {
                string[] textLines = text.Split(Environment.NewLine);
                foreach (string str in textLines)
                {
                    Console.WriteLine(str);
                }
            }
            else
            {
                if (File.Exists(fileName))
                {
                    Console.WriteLine($"Файл {fileName} уже существует!");
                }

                while (true)
                {
                    Console.Write("Хотите пересоздать файл с данным именем? (y/n): ");
                    char res = Console.ReadKey(true).KeyChar;
                    Console.WriteLine(res);

                    if (res == 'n' || res == 'N') return;
                    if (res == 'y' || res == 'Y') break;
                }

                using (FileStream fs = File.Create(fileName))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);

                    fs.Write(bytes, 0, bytes.Length);
                }
                Console.WriteLine($"Вспомогательная информация записана в в файл - {fileName}");
            }
        }
        /// <summary>
        /// Проверка расширения файла для его создания 
        /// </summary>
        public static IFile CheckExtention(string fileName, string? recLen = null)
        {
            if (fileName.EndsWith(".prd"))
            {
                if (recLen == null) throw new Exception("Укажите длину записи данных: Create <имя файла>(длина записи)");

                return new PRD(fileName, recLen);
            }
            else if (fileName.EndsWith(".prs"))
            {
                return new PRS(fileName);
            }
            else
            { 
                throw new Exception("Файл должен иметь расширение prd или prs");
            }
        }

        /// <summary>
        /// Проверка расширения (для всех остальных команд)
        /// </summary>
        public static IFile CheckExtention(string fileName)
        {
            if (fileName.EndsWith(".prd"))
            {
                return new PRD(fileName);
            }
            else if (fileName.EndsWith(".prs"))
            {
                return new PRS(fileName);
            }
            else
            { 
                throw new Exception("Файл должен иметь расширение prd или prs");
            }
        }

        static void Main(string[] args)
        {
            IFile currentFile = null;
            string message;
            Console.WriteLine("Система управления спецификациями (PRD)");

            // Основной цикл командной строки
            while (true)
            {
                Console.Write("PS> ");
                string commandLine = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(commandLine)) continue;

                string[] parts = commandLine.Split(new[] { ' ' }, 2);
                string command = parts[0].ToLower();
                string argument = parts.Length > 1 ? parts[1] : null;

                try
                {
                    switch (command)
                    {
                        case "create":
                            if (string.IsNullOrEmpty(argument))
                            {
                                Console.WriteLine("Ошибка: Укажите имя файла. Пример: create test.prd");
                                break;
                            }

                            string[] partsArgument = argument.Split(new[] { '(' }, 2);

                            string fileName = partsArgument[0];

                            string? recLen = null;

                            if (partsArgument.Length > 1)
                            {
                                recLen = partsArgument[1].Replace(")", "");
                            }

                            currentFile = CheckExtention(fileName, recLen);

                            currentFile.Create();
                            break;

                        case "open":
                            if (string.IsNullOrEmpty(argument))
                            {
                                Console.WriteLine("Ошибка: Укажите имя файла. Пример: open test.prd");
                                break;
                            }

                            currentFile = CheckExtention(argument);

                            currentFile.Open();
                            break;

                        case "input":
                            if ((currentFile == null) || !currentFile.IsOpen)
                            { 
                                throw new Exception("Файл не открыт");
                            }
                                
                            if (string.IsNullOrEmpty(argument))
                            {
                                throw new Exception("Формат: input <имя> <тип>");
                            }

                            message = currentFile.Input(argument);
                            Console.WriteLine(message);
                            break;

                        case "delete":
                            if ((currentFile == null) || !currentFile.IsOpen)
                            { 
                                throw new Exception("Файл не открыт");
                            }

                            if (string.IsNullOrEmpty(argument))
                            { 
                                throw new Exception("Формат: delete <имя>");
                            }
                                

                            message = currentFile.Delete(argument);
                            Console.WriteLine(message);
                            break;

                        case "print":
                            if ((currentFile == null) || !currentFile.IsOpen)
                            { 
                                throw new Exception("Файл не открыт");
                            }
                                
                            if (string.IsNullOrEmpty(argument))
                            {
                                Console.WriteLine("Формат: print <имя> или *");
                                break;
                            }

                            currentFile.Print(argument);
                            break;

                        case "printdev":
                            if ((currentFile == null) || !currentFile.IsOpen)
                            { 
                                throw new Exception("Файл не открыт");
                            }
                                
                            currentFile.PrintDev();
                            break;

                        case "restore":
                            if ((currentFile == null) || !currentFile.IsOpen)
                            { 
                                throw new Exception("Файл не открыт");
                            }


                            if (string.IsNullOrEmpty(argument))
                            { 
                                throw new Exception("Формат: Restore <имя> или *");
                            }
                                
                            currentFile.Restore(argument);
                            break;

                        case "help":
                            Help(argument);
                            break;

                        case "truncate":
                            if ((currentFile == null) || !currentFile.IsOpen)
                            { 
                                throw new Exception("Файл не открыт");
                            }

                            currentFile.Truncate();
                            break;

                        case "exit":
                            return;

                        default:
                            Console.WriteLine("Неизвестная команда. Доступные команды: create, open, input, delete, print, restore, exit");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }
    }
}