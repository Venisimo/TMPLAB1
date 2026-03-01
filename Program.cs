using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace TMPLAB1
{
    internal class Program
    {
        // Заголовок PRD
        const int SIZE_SIGNATURE = 2;
        const int SIZE_RECORDLEN = 2;
        const int SIZE_P_FIRSTREC = 4;
        const int SIZE_P_FREESPACE = 4;
        const int SIZE_NAMESPEC = 16;
        const int RECORD_NAME_MAXLEN = 64;
        const int HEADER_SIZE = SIZE_SIGNATURE + SIZE_RECORDLEN + SIZE_P_FIRSTREC + SIZE_P_FREESPACE + SIZE_NAMESPEC;

        // Смещения в заголовке
        const int OFFSET_RECORDLEN = SIZE_SIGNATURE;                     // 2
        const int OFFSET_P_FIRSTREC = OFFSET_RECORDLEN + SIZE_RECORDLEN; // 4
        const int OFFSET_P_FREESPACE = OFFSET_P_FIRSTREC + SIZE_P_FIRSTREC; // 8
        const int OFFSET_NAMESPEC = OFFSET_P_FREESPACE + SIZE_P_FREESPACE;  // 12

        enum Command { CMD_CREATE, CMD_OPEN, CMD_SAVE, CMD_UNKNOWN, CMD_INPUT, CMD_PRINT };
        enum Type { PRODUCT, ASSEMBLY, DETAIL, UNKNOWN };

        static Command GetCommandType(string cmd)
        {
            if (cmd == "create") return Command.CMD_CREATE;
            if (cmd == "open") return Command.CMD_OPEN;
            if (cmd == "save") return Command.CMD_SAVE;
            if (cmd == "input") return Command.CMD_INPUT;
            return Command.CMD_UNKNOWN;
        }

        static void Help(string fileName)
        {
            string[] lines =
                {
                    "Список команд:",
                    "Create <имя файла> - создает файл с расширением prd",
                    "Open <имя файла> - открывает указанный файл для работы с ним",
                    "Input (имя компонента, тип) Input (имя компонента, тип) — включает компонент в список. тип — одно из следующего: Изделие, Узел, Деталь.",
                    "Input (имя компонента/имя комплектующего) — включает комплектующее в спецификацию компонента.",
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

        static void Main(string[] args)
        {
            FileHeaderPRD currentFile = null;

            Console.WriteLine("Система управления спецификациями (PRD)");

            while (true)
            {
                Console.Write("PS> ");
                string commandLine = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(commandLine))
                    continue;

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
                            currentFile = new FileHeaderPRD();
                            currentFile.Create(argument);
                            break;

                        case "open":
                            if (string.IsNullOrEmpty(argument))
                            {
                                Console.WriteLine("Ошибка: Укажите имя файла. Пример: open test.prd");
                                break;
                            }
                            currentFile = new FileHeaderPRD();
                            currentFile.Open(argument);
                            break;

                        case "input":
                            if (currentFile == null || !currentFile.IsOpen)
                                throw new Exception("Файл не открыт");

                            if (string.IsNullOrEmpty(argument))
                                throw new Exception("Формат: input <имя> <тип>");

                            currentFile.Input(argument);
                            break;

                        case "delete":
                            if (currentFile == null || !currentFile.IsOpen)
                                throw new Exception("Файл не открыт");

                            if (string.IsNullOrEmpty(argument))
                                throw new Exception("Формат: delete <имя>");

                            currentFile.Delete(argument);
                            break;

                        case "print":
                            if (currentFile == null || !currentFile.IsOpen)
                                throw new Exception("Файл не открыт");

                            if (string.IsNullOrEmpty(argument))
                            {
                                Console.WriteLine("Формат: print <имя> или *");
                                break;
                            }

                            currentFile.Print(argument);
                            break;
                        case "printdev": // для делтального просмотра файла
                            if (currentFile == null || !currentFile.IsOpen)
                                throw new Exception("Файл не открыт");

                            if (string.IsNullOrEmpty(argument))
                            {
                                Console.WriteLine("Ошибка: Укажите имя файла. Пример: print test.prd");
                                break;
                            }

                            currentFile.PrintDev();
                            break;
                        case "restore":
                            if (currentFile == null || !currentFile.IsOpen)
                                throw new Exception("Файл не открыт");

                            if (string.IsNullOrEmpty(argument))
                                throw new Exception("Формат: Restore <имя> или *");

                            currentFile.Restore(argument);
                            break;
                        case "help":
                            Help(argument);

                            break;
                        case "truncate":
                            if (currentFile == null || !currentFile.IsOpen)
                                throw new Exception("Файл не открыт");

                            currentFile.Truncate();
                            break;

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