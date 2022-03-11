using AutoMapper;
using Chat.Web.Data;
using Chat.Web.Models;
using Chat.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Chat.Web.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        public readonly static List<UserViewModel> _Connections = new List<UserViewModel>();
        public readonly static List<GroupViewModel> _Groups = new List<GroupViewModel>();
        private readonly static Dictionary<string, string> _ConnectionsMap = new Dictionary<string, string>();

        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public ChatHub(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task SendPrivate(string receiverName, string message)
        {
            if (_ConnectionsMap.TryGetValue(receiverName, out string userId))
            {
                // Who is the sender;
                var sender = _Connections.Where(u => u.Username == IdentityName).First();

                if (!string.IsNullOrEmpty(message.Trim()))
                {
                    // Build the message
                    var messageViewModel = new MessageViewModel()
                    {
                        Content = Regex.Replace(message, @"(?i)<(?!img|a|/a|/img).*?>", string.Empty),
                        From = sender.FullName,
                        To = "",
                        Timestamp = DateTime.Now.ToLongTimeString()
                    };

                    // Send the message
                    await Clients.Client(userId).SendAsync("newMessage", messageViewModel);
                    await Clients.Caller.SendAsync("newMessage", messageViewModel);
                }
            }
        }

        public async Task SendToGroup(string GroupName, string message)
        {
            try
            {
                var user = _context.Users.Where(u => u.UserName == IdentityName).FirstOrDefault();
                var Group = _context.Group.Where(r => r.Name == GroupName).FirstOrDefault();

                if (!string.IsNullOrEmpty(message.Trim()))
                {
                    // Create and save message in database
                    var msg = new Message()
                    {
                        Content = Regex.Replace(message, @"(?i)<(?!img|a|/a|/img).*?>", string.Empty),
                        FromUser = user,
                        ToGroup = Group,
                        Timestamp = DateTime.Now
                    };
                    _context.Messages.Add(msg);
                    _context.SaveChanges();

                    // Broadcast the message
                    var messageViewModel = _mapper.Map<Message, MessageViewModel>(msg);
                    await Clients.Group(GroupName).SendAsync("newMessage", messageViewModel);
                }
            }
            catch (Exception)
            {
                await Clients.Caller.SendAsync("onError", "Message not send! Message should be 1-500 characters.");
            }
        }

        public async Task Join(string GroupName)
        {
            try
            {
                var user = _Connections.Where(u => u.Username == IdentityName).FirstOrDefault();
                if (user != null && user.CurrentGroup != GroupName)
                {
                    // Remove user from others list
                    if (!string.IsNullOrEmpty(user.CurrentGroup))
                        await Clients.OthersInGroup(user.CurrentGroup).SendAsync("removeUser", user);

                    // Join to new chat Group
                    await Leave(user.CurrentGroup);
                    await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
                    user.CurrentGroup = GroupName;

                    // Tell others to update their list of users
                    await Clients.OthersInGroup(GroupName).SendAsync("addUser", user);
                }
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("onError", "You failed to join the chat Group!" + ex.Message);
            }
        }

        public async Task Leave(string GroupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName);
        }

        public async Task CreateGroup(string GroupName)
        {
            try
            {

                // Accept: Letters, numbers and one space between words.
                Match match = Regex.Match(GroupName, @"^\w+( \w+)*$");
                if (!match.Success)
                {
                    await Clients.Caller.SendAsync("onError", "Invalid Group name!\nGroup name must contain only letters and numbers.");
                }
                else if (GroupName.Length < 5 || GroupName.Length > 100)
                {
                    await Clients.Caller.SendAsync("onError", "Group name must be between 5-100 characters!");
                }
                else if (_context.Group.Any(r => r.Name == GroupName))
                {
                    await Clients.Caller.SendAsync("onError", "Another chat Group with this name exists");
                }
                else
                {
                    // Create and save chat Group in database
                    var user = _context.Users.Where(u => u.UserName == IdentityName).FirstOrDefault();
                    var Group = new Models.Group()
                    {
                        Name = GroupName,
                        Admin = user
                    };
                    _context.Group.Add(Group);
                    _context.SaveChanges();

                    if (Group != null)
                    {
                        // Update Group list
                        var GroupViewModel = _mapper.Map<Models.Group, GroupViewModel>(Group);
                        _Groups.Add(GroupViewModel);
                        await Clients.All.SendAsync("addChatGroup", GroupViewModel);
                    }
                }
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("onError", "Couldn't create chat Group: " + ex.Message);
            }
        }

        public async Task DeleteGroup(string GroupName)
        {
            try
            {
                // Delete from database
                var Group = _context.Group.Include(r => r.Admin)
                    .Where(r => r.Name == GroupName && r.Admin.UserName == IdentityName).FirstOrDefault();
                _context.Group.Remove(Group);
                _context.SaveChanges();

                // Delete from list
                var GroupViewModel = _Groups.First(r => r.Name == GroupName);
                _Groups.Remove(GroupViewModel);

                // Move users back to Lobby
                await Clients.Group(GroupName).SendAsync("onGroupDeleted", string.Format("Group {0} has been deleted.\nYou are now moved to the Lobby!", GroupName));

                // Tell all users to update their Group list
                await Clients.All.SendAsync("removeChatGroup", GroupViewModel);
            }
            catch (Exception)
            {
                await Clients.Caller.SendAsync("onError", "Can't delete this chat Group. Only owner can delete this Group.");
            }
        }

        public IEnumerable<GroupViewModel> GetGroups()
        {
            // First run?
            if (_Groups.Count == 0)
            {
                foreach (var Group in _context.Group)
                {
                    var GroupViewModel = _mapper.Map<Models.Group, GroupViewModel>(Group);
                    _Groups.Add(GroupViewModel);
                }
            }

            return _Groups.ToList();
        }

        public IEnumerable<UserViewModel> GetUsers(string GroupName)
        {
            return _Connections.Where(u => u.CurrentGroup == GroupName).ToList();
        }

        public IEnumerable<MessageViewModel> GetMessageHistory(string GroupName)
        {
            var messageHistory = _context.Messages.Where(m => m.ToGroup.Name == GroupName)
                    .Include(m => m.FromUser)
                    .Include(m => m.ToGroup)
                    .OrderByDescending(m => m.Timestamp)
                    .Take(20)
                    .AsEnumerable()
                    .Reverse()
                    .ToList();

            return _mapper.Map<IEnumerable<Message>, IEnumerable<MessageViewModel>>(messageHistory);
        }

        public override Task OnConnectedAsync()
        {
            try
            {
                var user = _context.Users.Where(u => u.UserName == IdentityName).FirstOrDefault();
                var userViewModel = _mapper.Map<ApplicationUser, UserViewModel>(user);
                userViewModel.Device = GetDevice();
                userViewModel.CurrentGroup = "";

                if (!_Connections.Any(u => u.Username == IdentityName))
                {
                    _Connections.Add(userViewModel);
                    _ConnectionsMap.Add(IdentityName, Context.ConnectionId);
                }

                Clients.Caller.SendAsync("getProfileInfo", user.FullName, user.UserName);
            }
            catch (Exception ex)
            {
                Clients.Caller.SendAsync("onError", "OnConnected:" + ex.Message);
            }
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                var user = _Connections.Where(u => u.Username == IdentityName).First();
                _Connections.Remove(user);

                // Tell other users to remove you from their list
                Clients.OthersInGroup(user.CurrentGroup).SendAsync("removeUser", user);

                // Remove mapping
                _ConnectionsMap.Remove(user.Username);
            }
            catch (Exception ex)
            {
                Clients.Caller.SendAsync("onError", "OnDisconnected: " + ex.Message);
            }

            return base.OnDisconnectedAsync(exception);
        }

        private string IdentityName
        {
            get { return Context.User.Identity.Name; }
        }

        private string GetDevice()
        {
            var device = Context.GetHttpContext().Request.Headers["Device"].ToString();
            if (!string.IsNullOrEmpty(device) && (device.Equals("Desktop") || device.Equals("Mobile")))
                return device;

            return "Web";
        }
    }
}
