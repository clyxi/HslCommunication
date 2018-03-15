﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HslCommunication.Core;
using HslCommunication.Core.IMessage;
using System.Net.Sockets;

namespace HslCommunication.Profinet
{
    /// <summary>
    /// 使用了Fetch/Write协议来和西门子进行通讯，该种方法需要在PLC侧进行一些配置
    /// </summary>
    public class SiemensFetchWriteNet : NetworkDoubleBase<FetchWriteMessage,ReverseBytesTransform>
    {
        #region Constructor

        /// <summary>
        /// 实例化一个西门子的Fetch/Write协议的通讯对象
        /// </summary>
        public SiemensFetchWriteNet()
        {

        }
        

        #endregion

        #region Address Analysis

        /// <summary>
        /// 计算特殊的地址信息
        /// </summary>
        /// <param name="address">字符串信息</param>
        /// <returns>实际值</returns>
        private int CalculateAddressStarted(string address)
        {
            if (address.IndexOf('.') < 0)
            {
                return Convert.ToInt32(address) * 8;
            }
            else
            {
                string[] temp = address.Split('.');
                return Convert.ToInt32(temp[0]) * 8 + Convert.ToInt32(temp[1]);
            }
        }

        /// <summary>
        /// 解析数据地址，解析出地址类型，起始地址，DB块的地址
        /// </summary>
        /// <param name="address">数据地址</param>
        /// <returns>解析出地址类型，起始地址，DB块的地址</returns>
        private OperateResult<byte, int, ushort> AnalysisAddress(string address)
        {
            var result = new OperateResult<byte, int, ushort>();
            try
            {
                result.Content3 = 0;
                if (address[0] == 'I')
                {
                    result.Content1 = 0x81;
                    result.Content2 = CalculateAddressStarted(address.Substring(1));
                }
                else if (address[0] == 'Q')
                {
                    result.Content1 = 0x82;
                    result.Content2 = CalculateAddressStarted(address.Substring(1));
                }
                else if (address[0] == 'M')
                {
                    result.Content1 = 0x83;
                    result.Content2 = CalculateAddressStarted(address.Substring(1));
                }
                else if (address[0] == 'D' || address.Substring(0, 2) == "DB")
                {
                    result.Content1 = 0x84;
                    string[] adds = address.Split('.');
                    if (address[1] == 'B')
                    {
                        result.Content3 = Convert.ToUInt16(adds[0].Substring(2));
                    }
                    else
                    {
                        result.Content3 = Convert.ToUInt16(adds[0].Substring(1));
                    }

                    result.Content2 = CalculateAddressStarted(address.Substring(address.IndexOf('.') + 1));
                }
                else if (address[0] == 'T')
                {
                    result.Content1 = 0x1D;
                    result.Content2 = CalculateAddressStarted(address.Substring(1));
                }
                else if (address[0] == 'C')
                {
                    result.Content1 = 0x1C;
                    result.Content2 = CalculateAddressStarted(address.Substring(1));
                }
                else
                {
                    result.Message = "不支持的数据类型";
                    result.Content1 = 0;
                    result.Content2 = 0;
                    result.Content3 = 0;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                return result;
            }

            result.IsSuccess = true;
            return result;
        }


        #endregion

        #region Build Command

        /// <summary>
        /// 生成一个读取字数据指令头的通用方法
        /// </summary>
        /// <param name="address"></param>
        /// <param name="count"></param>
        /// <returns>携带有命令字节</returns>
        private OperateResult<byte[]> BuildReadCommand(string[] address, ushort[] count)
        {
            if (address == null) throw new NullReferenceException("address");
            if (count == null) throw new NullReferenceException("count");
            if (address.Length != count.Length) throw new Exception("两个参数的个数不统一");
            if (count.Length > 255) throw new Exception("读取的数组数量不允许大于255");

            var result = new OperateResult<byte[]>();
            int readCount = count.Length;

            byte[] _PLCCommand = new byte[19 + readCount * 12];

            // ======================================================================================
            // Header
            // 报文头
            _PLCCommand[0] = 0x03;
            _PLCCommand[1] = 0x00;
            // 长度
            _PLCCommand[2] = (byte)(_PLCCommand.Length / 256);
            _PLCCommand[3] = (byte)(_PLCCommand.Length % 256);
            // 固定
            _PLCCommand[4] = 0x02;
            _PLCCommand[5] = 0xF0;
            _PLCCommand[6] = 0x80;
            // 协议标识
            _PLCCommand[7] = 0x32;
            // 命令：发
            _PLCCommand[8] = 0x01;
            // redundancy identification (reserved): 0x0000;
            _PLCCommand[9] = 0x00;
            _PLCCommand[10] = 0x00;
            // protocol data unit reference; it’s increased by request event;
            _PLCCommand[11] = 0x00;
            _PLCCommand[12] = 0x01;
            // 参数命令数据总长度
            _PLCCommand[13] = (byte)((_PLCCommand.Length - 17) / 256);
            _PLCCommand[14] = (byte)((_PLCCommand.Length - 17) % 256);

            // 读取内部数据时为00，读取CPU型号为Data数据长度
            _PLCCommand[15] = 0x00;
            _PLCCommand[16] = 0x00;


            // ======================================================================================
            // Parameter

            // 读写指令，04读，05写
            _PLCCommand[17] = 0x04;
            // 读取数据块个数
            _PLCCommand[18] = (byte)readCount;


            for (int ii = 0; ii < readCount; ii++)
            {
                // 填充数据
                OperateResult<byte, int, ushort> analysis = AnalysisAddress(address[ii]);
                if (!analysis.IsSuccess)
                {
                    result.CopyErrorFromOther(analysis);
                    return result;
                }

                //===========================================================================================
                // 指定有效值类型
                _PLCCommand[19 + ii * 12] = 0x12;
                // 接下来本次地址访问长度
                _PLCCommand[20 + ii * 12] = 0x0A;
                // 语法标记，ANY
                _PLCCommand[21 + ii * 12] = 0x10;
                // 按字为单位
                _PLCCommand[22 + ii * 12] = 0x02;
                // 访问数据的个数
                _PLCCommand[23 + ii * 12] = (byte)(count[ii] / 256);
                _PLCCommand[24 + ii * 12] = (byte)(count[ii] % 256);
                // DB块编号，如果访问的是DB块的话
                _PLCCommand[25 + ii * 12] = (byte)(analysis.Content3 / 256);
                _PLCCommand[26 + ii * 12] = (byte)(analysis.Content3 % 256);
                // 访问数据类型
                _PLCCommand[27 + ii * 12] = analysis.Content1;
                // 偏移位置
                _PLCCommand[28 + ii * 12] = (byte)(analysis.Content2 / 256 / 256 % 256);
                _PLCCommand[29 + ii * 12] = (byte)(analysis.Content2 / 256 % 256);
                _PLCCommand[30 + ii * 12] = (byte)(analysis.Content2 % 256);
            }
            result.Content = _PLCCommand;
            result.IsSuccess = true;
            return result;
        }

        /// <summary>
        /// 生成一个位读取数据指令头的通用方法
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private OperateResult<byte[]> BuildBitReadCommand(string address)
        {
            var result = new OperateResult<byte[]>();

            byte[] _PLCCommand = new byte[31];
            // 报文头
            _PLCCommand[0] = 0x03;
            _PLCCommand[1] = 0x00;
            // 长度
            _PLCCommand[2] = (byte)(_PLCCommand.Length / 256);
            _PLCCommand[3] = (byte)(_PLCCommand.Length % 256);
            // 固定
            _PLCCommand[4] = 0x02;
            _PLCCommand[5] = 0xF0;
            _PLCCommand[6] = 0x80;
            _PLCCommand[7] = 0x32;
            // 命令：发
            _PLCCommand[8] = 0x01;
            // 标识序列号
            _PLCCommand[9] = 0x00;
            _PLCCommand[10] = 0x00;
            _PLCCommand[11] = 0x00;
            _PLCCommand[12] = 0x01;
            // 命令数据总长度
            _PLCCommand[13] = (byte)((_PLCCommand.Length - 17) / 256);
            _PLCCommand[14] = (byte)((_PLCCommand.Length - 17) % 256);

            _PLCCommand[15] = 0x00;
            _PLCCommand[16] = 0x00;

            // 命令起始符
            _PLCCommand[17] = 0x04;
            // 读取数据块个数
            _PLCCommand[18] = 0x01;


            // 填充数据
            OperateResult<byte, int, ushort> analysis = AnalysisAddress(address);
            if (!analysis.IsSuccess)
            {
                result.CopyErrorFromOther(analysis);
                return result;
            }

            //===========================================================================================
            // 读取地址的前缀
            _PLCCommand[19] = 0x12;
            _PLCCommand[20] = 0x0A;
            _PLCCommand[21] = 0x10;
            // 读取的数据时位
            _PLCCommand[22] = 0x01;
            // 访问数据的个数
            _PLCCommand[23] = 0x00;
            _PLCCommand[24] = 0x01;
            // DB块编号，如果访问的是DB块的话
            _PLCCommand[25] = (byte)(analysis.Content3 / 256);
            _PLCCommand[26] = (byte)(analysis.Content3 % 256);
            // 访问数据类型
            _PLCCommand[27] = analysis.Content1;
            // 偏移位置
            _PLCCommand[28] = (byte)(analysis.Content2 / 256 / 256 % 256);
            _PLCCommand[29] = (byte)(analysis.Content2 / 256 % 256);
            _PLCCommand[30] = (byte)(analysis.Content2 % 256);

            result.Content = _PLCCommand;
            result.IsSuccess = true;
            return result;
        }


        /// <summary>
        /// 生成一个写入字节数据的指令
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        private OperateResult<byte[]> BuildWriteByteCommand(string address, byte[] data)
        {
            if (data == null) data = new byte[0];
            var result = new OperateResult<byte[]>();

            OperateResult<byte, int, ushort> analysis = AnalysisAddress(address);
            if (!analysis.IsSuccess)
            {
                result.CopyErrorFromOther(analysis);
                return result;
            }

            byte[] _PLCCommand = new byte[35 + data.Length];
            _PLCCommand[0] = 0x03;
            _PLCCommand[1] = 0x00;
            // 长度
            _PLCCommand[2] = (byte)((35 + data.Length) / 256);
            _PLCCommand[3] = (byte)((35 + data.Length) % 256);
            // 固定
            _PLCCommand[4] = 0x02;
            _PLCCommand[5] = 0xF0;
            _PLCCommand[6] = 0x80;
            _PLCCommand[7] = 0x32;
            // 命令 发
            _PLCCommand[8] = 0x01;
            // 标识序列号
            _PLCCommand[9] = 0x00;
            _PLCCommand[10] = 0x00;
            _PLCCommand[11] = 0x00;
            _PLCCommand[12] = 0x01;
            // 固定
            _PLCCommand[13] = 0x00;
            _PLCCommand[14] = 0x0E;
            // 写入长度+4
            _PLCCommand[15] = (byte)((4 + data.Length) / 256);
            _PLCCommand[16] = (byte)((4 + data.Length) % 256);
            // 读写指令
            _PLCCommand[17] = 0x05;
            // 写入数据块个数
            _PLCCommand[18] = 0x01;
            // 固定，返回数据长度
            _PLCCommand[19] = 0x12;
            _PLCCommand[20] = 0x0A;
            _PLCCommand[21] = 0x10;
            // 写入方式，1是按位，2是按字
            _PLCCommand[22] = 0x02;
            // 写入数据的个数
            _PLCCommand[23] = (byte)(data.Length / 256);
            _PLCCommand[24] = (byte)(data.Length % 256);
            // DB块编号，如果访问的是DB块的话
            _PLCCommand[25] = (byte)(analysis.Content3 / 256);
            _PLCCommand[26] = (byte)(analysis.Content3 % 256);
            // 写入数据的类型
            _PLCCommand[27] = analysis.Content1;
            // 偏移位置
            _PLCCommand[28] = (byte)(analysis.Content2 / 256 / 256 % 256); ;
            _PLCCommand[29] = (byte)(analysis.Content2 / 256 % 256);
            _PLCCommand[30] = (byte)(analysis.Content2 % 256);
            // 按字写入
            _PLCCommand[31] = 0x00;
            _PLCCommand[32] = 0x04;
            // 按位计算的长度
            _PLCCommand[33] = (byte)(data.Length * 8 / 256);
            _PLCCommand[34] = (byte)(data.Length * 8 % 256);

            data.CopyTo(_PLCCommand, 35);

            result.Content = _PLCCommand;
            result.IsSuccess = true;
            return result;
        }

        /// <summary>
        /// 生成一个写入位数据的指令
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        private OperateResult<byte[]> BuildWriteBitCommand(string address, bool data)
        {
            var result = new OperateResult<byte[]>();

            OperateResult<byte, int, ushort> analysis = AnalysisAddress(address);
            if (!analysis.IsSuccess)
            {
                result.CopyErrorFromOther(analysis);
                return result;
            }


            byte[] buffer = new byte[1];
            buffer[0] = data ? (byte)0x01 : (byte)0x00;

            byte[] _PLCCommand = new byte[35 + buffer.Length];
            _PLCCommand[0] = 0x03;
            _PLCCommand[1] = 0x00;
            // 长度
            _PLCCommand[2] = (byte)((35 + buffer.Length) / 256);
            _PLCCommand[3] = (byte)((35 + buffer.Length) % 256);
            // 固定
            _PLCCommand[4] = 0x02;
            _PLCCommand[5] = 0xF0;
            _PLCCommand[6] = 0x80;
            _PLCCommand[7] = 0x32;
            // 命令 发
            _PLCCommand[8] = 0x01;
            // 标识序列号
            _PLCCommand[9] = 0x00;
            _PLCCommand[10] = 0x00;
            _PLCCommand[11] = 0x00;
            _PLCCommand[12] = 0x01;
            // 固定
            _PLCCommand[13] = 0x00;
            _PLCCommand[14] = 0x0E;
            // 写入长度+4
            _PLCCommand[15] = (byte)((4 + buffer.Length) / 256);
            _PLCCommand[16] = (byte)((4 + buffer.Length) % 256);
            // 命令起始符
            _PLCCommand[17] = 0x05;
            // 写入数据块个数
            _PLCCommand[18] = 0x01;
            _PLCCommand[19] = 0x12;
            _PLCCommand[20] = 0x0A;
            _PLCCommand[21] = 0x10;
            // 写入方式，1是按位，2是按字
            _PLCCommand[22] = 0x01;
            // 写入数据的个数
            _PLCCommand[23] = (byte)(buffer.Length / 256);
            _PLCCommand[24] = (byte)(buffer.Length % 256);
            // DB块编号，如果访问的是DB块的话
            _PLCCommand[25] = (byte)(analysis.Content3 / 256);
            _PLCCommand[26] = (byte)(analysis.Content3 % 256);
            // 写入数据的类型
            _PLCCommand[27] = analysis.Content1;
            // 偏移位置
            _PLCCommand[28] = (byte)(analysis.Content2 / 256 / 256);
            _PLCCommand[29] = (byte)(analysis.Content2 / 256);
            _PLCCommand[30] = (byte)(analysis.Content2 % 256);
            // 按位写入
            _PLCCommand[31] = 0x00;
            _PLCCommand[32] = 0x03;
            // 按位计算的长度
            _PLCCommand[33] = (byte)(buffer.Length / 256);
            _PLCCommand[34] = (byte)(buffer.Length % 256);

            buffer.CopyTo(_PLCCommand, 35);

            result.Content = _PLCCommand;
            result.IsSuccess = true;
            return result;
        }



        #endregion

        #region Read OrderNumber

        /// <summary>
        /// 从PLC读取订货号信息
        /// </summary>
        /// <returns></returns>
        public OperateResult<string> ReadOrderNumber()
        {
            OperateResult<string> result = new OperateResult<string>();
            OperateResult<byte[]> read = ReadFromCoreServer(plcOrderNumber);
            if (read.IsSuccess)
            {
                if (read.Content.Length > 100)
                {
                    result.IsSuccess = true;
                    result.Content = Encoding.ASCII.GetString(read.Content, 71, 20);
                }
            }

            if (!result.IsSuccess)
            {
                result.CopyErrorFromOther(read);
            }

            return result;
        }

        #endregion

        #region Customer Support

        /// <summary>
        /// 读取自定义的数据类型，只要规定了写入和解析规则
        /// </summary>
        /// <typeparam name="T">类型名称</typeparam>
        /// <param name="address">起始地址</param>
        /// <returns></returns>
        public OperateResult<T> Read<T>(string address) where T : IDataTransfer, new()
        {
            OperateResult<T> result = new OperateResult<T>();
            T Content = new T();
            OperateResult<byte[]> read = Read(address, Content.ReadCount);
            if (read.IsSuccess)
            {
                Content.ParseSource(read.Content);
                result.Content = Content;
                result.IsSuccess = true;
            }
            else
            {
                result.ErrorCode = read.ErrorCode;
                result.Message = read.Message;
            }
            return result;
        }

        /// <summary>
        /// 写入自定义的数据类型到PLC去，只要规定了生成字节的方法即可
        /// </summary>
        /// <typeparam name="T">自定义类型</typeparam>
        /// <param name="address">起始地址</param>
        /// <param name="data">实例对象</param>
        /// <returns></returns>
        public OperateResult Write<T>(string address, T data) where T : IDataTransfer, new()
        {
            return Write(address, data.ToSource());
        }


        #endregion

        #region Read Support


        /// <summary>
        /// 从PLC读取数据，地址格式为I100，Q100，DB20.100，M100，以字节为单位
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <param name="count">读取的数量，以字节为单位</param>
        /// <returns></returns>
        public OperateResult<byte[]> Read(string address, ushort count)
        {
            return Read(new string[] { address }, new ushort[] { count });
        }

        /// <summary>
        /// 从PLC读取数据，地址格式为I100，Q100，DB20.100，M100，以位为单位
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        private OperateResult<byte[]> ReadBitFromPLC(string address)
        {
            OperateResult<byte[]> result = new OperateResult<byte[]>();

            OperateResult<byte[]> command = BuildBitReadCommand(address);
            if (!command.IsSuccess)
            {
                result.CopyErrorFromOther(command);
                return result;
            }

            OperateResult<byte[]> read = ReadFromCoreServer(command.Content);
            if (read.IsSuccess)
            {
                int receiveCount = 1;

                if (read.Content.Length >= 21 && read.Content[20] == 1)
                {
                    // 分析结果
                    byte[] buffer = new byte[receiveCount];

                    if (22 < read.Content.Length)
                    {
                        if (read.Content[21] == 0xFF &&
                            read.Content[22] == 0x03)
                        {
                            // 有数据
                            buffer[0] = read.Content[25];
                        }
                    }

                    result.Content = buffer;
                    result.IsSuccess = true;
                }
                else
                {
                    result.ErrorCode = read.ErrorCode;
                    result.Message = "数据块长度校验失败";
                }
            }
            else
            {
                result.ErrorCode = read.ErrorCode;
                result.Message = read.Message;
            }

            return result;
        }


        /// <summary>
        /// 一次性从PLC获取所有的数据，按照先后顺序返回一个统一的Buffer，需要按照顺序处理，两个数组长度必须一致
        /// </summary>
        /// <param name="address">起始地址数组</param>
        /// <param name="count">数据长度数组</param>
        /// <returns></returns>
        /// <exception cref="NullReferenceException"></exception>
        public OperateResult<byte[]> Read(string[] address, ushort[] count)
        {
            OperateResult<byte[]> result = new OperateResult<byte[]>();

            OperateResult<byte[]> command = BuildReadCommand(address, count);
            if (!command.IsSuccess)
            {
                result.CopyErrorFromOther(command);
                return result;
            }

            OperateResult<byte[]> read = ReadFromCoreServer(command.Content);
            if (read.IsSuccess)
            {
                int receiveCount = 0;
                for (int i = 0; i < count.Length; i++)
                {
                    receiveCount += count[i];
                }

                if (read.Content.Length >= 21 && read.Content[20] == count.Length)
                {
                    // 分析结果
                    byte[] buffer = new byte[receiveCount];
                    int kk = 0;
                    int ll = 0;
                    for (int ii = 21; ii < read.Content.Length; ii++)
                    {
                        if ((ii + 1) < read.Content.Length)
                        {
                            if (read.Content[ii] == 0xFF &&
                                read.Content[ii + 1] == 0x04)
                            {
                                // 有数据
                                Array.Copy(read.Content, ii + 4, buffer, ll, count[kk]);
                                ii += count[kk] + 3;
                                ll += count[kk];
                                kk++;
                            }
                        }
                    }

                    result.Content = buffer;
                    result.IsSuccess = true;
                }
                else
                {
                    result.ErrorCode = read.ErrorCode;
                    result.Message = "数据块长度校验失败";
                }
            }
            else
            {
                result.ErrorCode = read.ErrorCode;
                result.Message = read.Message;
            }

            return result;
        }


        /// <summary>
        /// 读取指定地址的bool数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<bool> ReadBool(string address)
        {
            return GetBoolResultFromBytes(ReadBitFromPLC(address));
        }


        /// <summary>
        /// 读取指定地址的byte数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<byte> ReadByte(string address)
        {
            return GetByteResultFromBytes(Read(address, 1));
        }


        /// <summary>
        /// 读取指定地址的short数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<short> ReadShort(string address)
        {
            return GetInt16ResultFromBytes(Read(address, 2));
        }


        /// <summary>
        /// 读取指定地址的ushort数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<ushort> ReadUShort(string address)
        {
            return GetUInt16ResultFromBytes(Read(address, 2));
        }

        /// <summary>
        /// 读取指定地址的int数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<int> ReadInt(string address)
        {
            return GetInt32ResultFromBytes(Read(address, 4));
        }

        /// <summary>
        /// 读取指定地址的uint数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<uint> ReadUInt(string address)
        {
            return GetUInt32ResultFromBytes(Read(address, 4));
        }

        /// <summary>
        /// 读取指定地址的float数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<float> ReadFloat(string address)
        {
            return GetSingleResultFromBytes(Read(address, 4));
        }

        /// <summary>
        /// 读取指定地址的long数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<long> ReadLong(string address)
        {
            return GetInt64ResultFromBytes(Read(address, 8));
        }

        /// <summary>
        /// 读取指定地址的ulong数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<ulong> ReadULong(string address)
        {
            return GetUInt64ResultFromBytes(Read(address, 8));
        }

        /// <summary>
        /// 读取指定地址的double数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <returns></returns>
        public OperateResult<double> ReadDouble(string address)
        {
            return GetDoubleResultFromBytes(Read(address, 8));
        }

        /// <summary>
        /// 读取地址地址的String数据
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <param name="length">字符串长度</param>
        /// <returns></returns>
        public OperateResult<string> ReadString(string address, ushort length)
        {
            return GetStringResultFromBytes(Read(address, length));
        }


        ///// <summary>
        ///// 读取多个DB块字节(超过协议报文222字节限制会自动处理)
        ///// </summary>
        ///// <param name="DB">DB块地址,例如DB1</param>
        ///// <param name="numBytes">要读的字节数</param>
        ///// <param name="startByteAdr">起始地址</param>
        ///// <returns></returns>
        //public OperateResult<byte[]> ReadMultipleBytes( string DB, int numBytes, int startByteAdr = 0 )
        //{
        //    OperateResult<byte[]> result = new OperateResult<byte[]>( );
        //    int index = startByteAdr;
        //    List<byte> resultBytes = new List<byte>( );
        //    while (numBytes > 0)
        //    {
        //        string db = String.Format( "{0}.{1}", DB, index.ToString( ) );
        //        int maxToRead = Math.Min( numBytes, 200 );
        //        OperateResult<byte[]> read = ReadFromPLC( db, (ushort)maxToRead );
        //        if (read == null)
        //            return new OperateResult<byte[]>( );
        //        resultBytes.AddRange( read.Content );
        //        numBytes -= maxToRead;
        //        index += maxToRead;
        //    }
        //    result.Content = resultBytes.ToArray( );
        //    return result;
        //}

        #endregion

        #region Write Base


        /// <summary>
        /// 将数据写入到PLC数据，地址格式为I100，Q100，DB20.100，M100，以字节为单位
        /// </summary>
        /// <param name="address">起始地址，格式为I100，M100，Q100，DB20.100</param>
        /// <param name="data">写入的数据，长度根据data的长度来指示</param>
        /// <returns></returns>
        public OperateResult Write(string address, byte[] data)
        {
            OperateResult result = new OperateResult();

            OperateResult<byte[]> command = BuildWriteByteCommand(address, data);
            if (!command.IsSuccess)
            {
                result.CopyErrorFromOther(command);
                return result;
            }


            OperateResult<byte[]> write = ReadFromCoreServer(command.Content);
            if (write.IsSuccess)
            {
                if (write.Content[write.Content.Length - 1] != 0xFF)
                {
                    // 写入异常
                    result.Message = "写入数据异常，代号为：" + write.Content[write.Content.Length - 1].ToString();
                }
                else
                {
                    result.IsSuccess = true;  // 写入成功
                }
            }
            else
            {
                result.ErrorCode = write.ErrorCode;
                result.Message = write.Message;
            }
            return result;
        }


        /// <summary>
        /// 写入PLC的一个位，例如"M100.6"，"I100.7"，"Q100.0"，"DB20.100.0"，如果只写了"M100"默认为"M100.0
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public OperateResult Write(string address, bool data)
        {
            OperateResult result = new OperateResult();

            // 生成指令
            OperateResult<byte[]> command = BuildWriteBitCommand(address, data);
            if (!command.IsSuccess)
            {
                result.CopyErrorFromOther(command);
                return result;
            }

            OperateResult<byte[]> write = ReadFromCoreServer(command.Content);
            if (write.IsSuccess)
            {
                if (write.Content[write.Content.Length - 1] != 0xFF)
                {
                    // 写入异常
                    result.Message = "写入数据异常，代号为：" + write.Content[write.Content.Length - 1].ToString();
                }
                else
                {
                    result.IsSuccess = true;  // 写入成功
                }
            }
            else
            {
                result.ErrorCode = write.ErrorCode;
                result.Message = write.Message;
            }
            return result;
        }


        #endregion

        #region Write String


        /// <summary>
        /// 向PLC中写入字符串，编码格式为ASCII
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回读取结果</returns>
        public OperateResult WriteAsciiString(string address, string data)
        {
            byte[] temp = Encoding.ASCII.GetBytes(data);
            return Write(address, temp);
        }

        /// <summary>
        /// 向PLC中写入指定长度的字符串,超出截断，不够补0，编码格式为ASCII
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <param name="length">指定的字符串长度，必须大于0</param>
        /// <returns>返回读取结果</returns>
        public OperateResult WriteAsciiString(string address, string data, int length)
        {
            byte[] temp = Encoding.ASCII.GetBytes(data);
            temp = BasicFramework.SoftBasic.ArrayExpandToLength(temp, length);
            return Write(address, temp);
        }

        /// <summary>
        /// 向PLC中写入字符串，编码格式为Unicode
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回读取结果</returns>
        public OperateResult WriteUnicodeString(string address, string data)
        {
            byte[] temp = Encoding.Unicode.GetBytes(data);
            return Write(address, temp);
        }

        /// <summary>
        /// 向PLC中写入指定长度的字符串,超出截断，不够补0，编码格式为Unicode
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <param name="length">指定的字符串长度，必须大于0</param>
        /// <returns>返回读取结果</returns>
        public OperateResult WriteUnicodeString(string address, string data, int length)
        {
            byte[] temp = Encoding.Unicode.GetBytes(data);
            temp = BasicFramework.SoftBasic.ArrayExpandToLength(temp, length * 2);
            return Write(address, temp);
        }

        #endregion

        #region Write bool[]

        /// <summary>
        /// 向PLC中写入bool数组，返回值说明，比如你写入M100,那么data[0]对应M100.0
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据，长度为8的倍数</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, bool[] data)
        {
            return Write(address, BasicFramework.SoftBasic.BoolArrayToByte(data));
        }


        #endregion

        #region Write Byte

        /// <summary>
        /// 向PLC中写入byte数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns></returns>
        public OperateResult Write(string address, byte data)
        {
            return Write(address, new byte[] { data });
        }

        #endregion

        #region Write Short

        /// <summary>
        /// 向PLC中写入short数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, short[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入short数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, short data)
        {
            return Write(address, new short[] { data });
        }

        #endregion

        #region Write UShort


        /// <summary>
        /// 向PLC中写入ushort数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, ushort[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }


        /// <summary>
        /// 向PLC中写入ushort数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, ushort data)
        {
            return Write(address, new ushort[] { data });
        }


        #endregion

        #region Write Int

        /// <summary>
        /// 向PLC中写入int数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, int[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入int数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, int data)
        {
            return Write(address, new int[] { data });
        }

        #endregion

        #region Write UInt

        /// <summary>
        /// 向PLC中写入uint数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, uint[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入uint数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, uint data)
        {
            return Write(address, new uint[] { data });
        }

        #endregion

        #region Write Float

        /// <summary>
        /// 向PLC中写入float数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, float[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入float数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, float data)
        {
            return Write(address, new float[] { data });
        }


        #endregion

        #region Write Long

        /// <summary>
        /// 向PLC中写入long数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, long[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入long数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, long data)
        {
            return Write(address, new long[] { data });
        }

        #endregion

        #region Write ULong

        /// <summary>
        /// 向PLC中写入ulong数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, ulong[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入ulong数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult Write(string address, ulong data)
        {
            return Write(address, new ulong[] { data });
        }

        #endregion

        #region Write Double

        /// <summary>
        /// 向PLC中写入double数组，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult WriteIntoPLC(string address, double[] data)
        {
            return Write(address, ByteTransform.TransByte(data));
        }

        /// <summary>
        /// 向PLC中写入double数据，返回值说明
        /// </summary>
        /// <param name="address">要写入的数据地址</param>
        /// <param name="data">要写入的实际数据</param>
        /// <returns>返回写入结果</returns>
        public OperateResult WriteIntoPLC(string address, double data)
        {
            return WriteIntoPLC(address, new double[] { data });
        }

        #endregion

        #region Head Codes

        private byte[] plcHead1 = new byte[22]
        {
                0x03,  // 01 RFC1006 Header
                0x00,  // 02 通常为 0
                0x00,  // 03 数据长度，高位
                0x16,  // 04 数据长度，地位
                0x11,  // 05 连接类型0x11:tcp  0x12 ISO-on-TCP
                0xE0,  // 06 主动建立连接
                0x00,  // 07 本地接口ID
                0x00,  // 08 主动连接时为0
                0x00,  // 09 该参数未使用
                0x01,  // 10 
                0x00,  // 11
                0xC0,  // 12
                0x01,  // 13
                0x0A,  // 14
                0xC1,  // 15
                0x02,  // 16
                0x01,  // 17
                0x00,  // 18
                0xC2,  // 19 指示cpu
                0x02,  // 20
                0x01,  // 21
                0x00   // 22
        };
        private byte[] plcHead2 = new byte[25]
        {
                0x03,
                0x00,
                0x00,
                0x19,
                0x02,
                0xF0,
                0x80,
                0x32,
                0x01,
                0x00,
                0x00,
                0x04,
                0x00,
                0x00,
                0x08,
                0x00,
                0x00,
                0xF0,  // 设置通讯
                0x00,
                0x00,
                0x01,
                0x00,
                0x01,
                0x01,
                0xE0
        };
        private byte[] plcOrderNumber = new byte[]
        {
            0x03,
            0x00,
            0x00,
            0x21,
            0x02,
            0xF0,
            0x80,
            0x32,
            0x07,
            0x00,
            0x00,
            0x00,
            0x01,
            0x00,
            0x08,
            0x00,
            0x08,
            0x00,
            0x01,
            0x12,
            0x04,
            0x11,
            0x44,
            0x01,
            0x00,
            0xFF,
            0x09,
            0x00,
            0x04,
            0x00,
            0x11,
            0x00,
            0x00
        };
        private SiemensPLCS CurrentPlc = SiemensPLCS.S1200;
        private byte[] plcHead1_200smart = new byte[22]
        {
            0x03,  // 01 RFC1006 Header             
            0x00,  // 02 通常为 0             
            0x00,  // 03 数据长度，高位            
            0x16,  // 04 数据长度，地位           
            0x11,  // 05 连接类型0x11:tcp  0x12 ISO-on-TCP               
            0xE0,  // 06 主动建立连接              
            0x00,  // 07 本地接口ID               
            0x00,  // 08 主动连接时为0              
            0x00,  // 09 该参数未使用              
            0x01,  // 10            
            0x00,  // 11          
            0xC1,  // 12           
            0x02,  // 13          
            0x10,  // 14             
            0x00,  // 15            
            0xC2,  // 16             
            0x02,  // 17           
            0x03,  // 18            
            0x00,  // 19 指示cpu     
            0xC0,  // 20              
            0x01,  // 21            
            0x0A   // 22       
        };
        private byte[] plcHead2_200smart = new byte[25]
        {
            0x03,
            0x00,
            0x00,
            0x19,
            0x02,
            0xF0,
            0x80,
            0x32,
            0x01,
            0x00,
            0x00,
            0xCC,
            0xC1,
            0x00,
            0x08,
            0x00,
            0x00,
            0xF0,  // 设置通讯      
            0x00,
            0x00,
            0x01,
            0x00,
            0x01,
            0x03,
            0xC0
        };

        #endregion

        #region Object Override

        /// <summary>
        /// 获取当前对象的字符串标识形式
        /// </summary>
        /// <returns>字符串信息</returns>
        public override string ToString()
        {
            return "SiemensS7Net";
        }

        #endregion



    }
}