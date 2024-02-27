using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace LoahDB.UWP
{
    /// <summary>
    /// By creating a new object of this class, a new file for your information named key is created in your application data folder and you can read or change its information.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Loah<T>
    {
        private StorageFolder storageFolder { get;  }
        private string key { get; set; }
        StorageFile loahFile;
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key">Database name</param>

        public Loah(string key)
        {
            storageFolder =
                ApplicationData.Current.LocalFolder;
            this.key=key;
        }
        private async void GetFile(string key)
        {
            
            if ( await storageFolder.TryGetItemAsync(key+".json")!=null)
            {
                loahFile =
                        await storageFolder.GetFileAsync(key+".json");
            }
            else
            {
                loahFile =
                     await storageFolder.CreateFileAsync(key+".json",
                       CreationCollisionOption.ReplaceExisting);
            }
            
           
        }
        /// <summary>
        /// This function helps you to get data from database.
        /// </summary>
        /// <returns>An object of previously defined type</returns>
        public async Task<T> GetAsync()
        {
            T jsonObject = (T)(object)null;
            GetFile(key);
            string json = await FileIO.ReadTextAsync(loahFile);
            jsonObject =JsonConvert.DeserializeObject<T>(json);
            if (jsonObject != null)
            {
                return jsonObject ;
            }
            else
            {
                return (T)(object)null;
            }

        }
        /// <summary>
        /// This function helps you to add and update data into database.
        /// </summary>
        /// <param name="obj"></param>
        public async Task SetAsync(T obj)
        {
            GetFile(key);
            string convertedJson = JsonConvert.SerializeObject(obj);
            await FileIO.WriteTextAsync(loahFile, convertedJson);
            
        }
        /// <summary>
        /// This function deletes the database with this key if it exists.
        /// </summary>
        public async Task DeleteAsync()
        {
            GetFile(key);
            await loahFile.DeleteAsync();

        }
        /// <summary>
        /// This function informs you about the existence of the database with this key.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> ExistsAsync(string ekey)
        {
            if (await storageFolder.TryGetItemAsync(ekey+".json")!=null)
            {
                return true;
            }
            else
            {
                return false;
            }
         
        }
    }
   
}
