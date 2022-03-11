$(document).ready(function () {
    var connection = new signalR.HubConnectionBuilder().withUrl("/chatHub").build();

    connection.start().then(function () {
        console.log('SignalR Started...')
        viewModel.GroupList();
        viewModel.userList();
        setTimeout(function () {
            if (viewModel.chatGroups().length > 0) {
                viewModel.joinGroup(viewModel.chatGroups()[0]);
            }
        }, 250);
    }).catch(function (err) {
        return console.error(err);
    });

    connection.on("newMessage", function (messageView) {
        var isMine = messageView.from === viewModel.myName();
        var message = new ChatMessage(messageView.content, messageView.timestamp, messageView.from, isMine, messageView.avatar);
        viewModel.chatMessages.push(message);
        $(".chat-body").animate({ scrollTop: $(".chat-body")[0].scrollHeight }, 1000);
    });

    connection.on("getProfileInfo", function (displayName, userName) {
        viewModel.myName(displayName);
    });

    connection.on("addUser", function (user) {
        viewModel.userAdded(new ChatUser(user.username, user.fullName, user.avatar, user.currentGroup, user.device));
    });

    connection.on("removeUser", function (user) {
        viewModel.userRemoved(user.username);
    });

    connection.on("addChatGroup", function (Group) {
        viewModel.GroupAdded(new ChatGroup(Group.id, Group.name));
    });

    connection.on("removeChatGroup", function (Group) {
        viewModel.GroupDeleted(Group.id);
    });

    connection.on("onError", function (message) {
        viewModel.serverInfoMessage(message);
        $("#errorAlert").removeClass("d-none").show().delay(5000).fadeOut(500);
    });

    connection.on("onGroupDeleted", function (message) {
        viewModel.serverInfoMessage(message);
        $("#errorAlert").removeClass("d-none").show().delay(5000).fadeOut(500);

        if (viewModel.chatGroups().length - 1 == 0) {
            viewModel.joinedGroup("");
        }
        else {
            // Join to the first Group in list
            $("ul#Group-list li a")[0].click();
        }
    });

    function AppViewModel() {
        var self = this;

        self.message = ko.observable("");
        self.chatGroups = ko.observableArray([]);
        self.chatUsers = ko.observableArray([]);
        self.chatMessages = ko.observableArray([]);
        self.joinedGroup = ko.observable("");
        self.joinedGroupId = ko.observable("");
        self.serverInfoMessage = ko.observable("");
        self.myName = ko.observable("");
        self.myAvatar = ko.observable("avatar1.png");
        self.onEnter = function (d, e) {
            if (e.keyCode === 13) {
                self.sendNewMessage();
            }
            return true;
        }
        self.filter = ko.observable("");
        self.filteredChatUsers = ko.computed(function () {
            if (!self.filter()) {
                return self.chatUsers();
            } else {
                return ko.utils.arrayFilter(self.chatUsers(), function (user) {
                    var displayName = user.displayName().toLowerCase();
                    return displayName.includes(self.filter().toLowerCase());
                });
            }
        });

        self.sendNewMessage = function () {
            var text = self.message();
            if (text.startsWith("/")) {
                var receiver = text.substring(text.indexOf("(") + 1, text.indexOf(")"));
                var message = text.substring(text.indexOf(")") + 1, text.length);

                if (receiver.length > 0 && message.length > 0)
                    connection.invoke("SendPrivate", receiver.trim(), message.trim());
            }
            else {
                if (self.joinedGroup().length > 0 && self.message().length > 0)
                    connection.invoke("SendToGroup", self.joinedGroup(), self.message());
            }

            self.message("");
        }

        self.joinGroup = function (Group) {
            connection.invoke("Join", Group.name()).then(function () {
                self.joinedGroup(Group.name());
                self.joinedGroupId(Group.id());
                self.userList();
                self.messageHistory();
            });
        }

        self.GroupList = function () {
            connection.invoke("GetGroups").then(function (result) {
                self.chatGroups.removeAll();
                for (var i = 0; i < result.length; i++) {
                    self.chatGroups.push(new ChatGroup(result[i].id, result[i].name));
                }
            });
        }

        self.userList = function () {
            connection.invoke("GetUsers", self.joinedGroup()).then(function (result) {
                self.chatUsers.removeAll();
                for (var i = 0; i < result.length; i++) {
                    self.chatUsers.push(new ChatUser(result[i].username,
                        result[i].fullName,
                        result[i].avatar,
                        result[i].currentGroup,
                        result[i].device))
                }
            });
        }

        self.createGroup = function () {
            var name = $("#GroupName").val();
            connection.invoke("CreateGroup", name);
        }

        self.deleteGroup = function () {
            connection.invoke("DeleteGroup", self.joinedGroup());
        }

        self.messageHistory = function () {
            connection.invoke("GetMessageHistory", self.joinedGroup()).then(function (result) {
                self.chatMessages.removeAll();
                for (var i = 0; i < result.length; i++) {
                    var isMine = result[i].from == self.myName();
                    self.chatMessages.push(new ChatMessage(result[i].content,
                        result[i].timestamp,
                        result[i].from,
                        isMine,
                        result[i].avatar))
                }

                $(".chat-body").animate({ scrollTop: $(".chat-body")[0].scrollHeight }, 1000);
            });
        }

        self.GroupAdded = function (Group) {
            self.chatGroups.push(Group);
        }

        self.GroupDeleted = function (id) {
            var temp;
            ko.utils.arrayForEach(self.chatGroups(), function (Group) {
                if (Group.id() == id)
                    temp = Group;
            });
            self.chatGroups.remove(temp);
        }

        self.userAdded = function (user) {
            self.chatUsers.push(user);
        }

        self.userRemoved = function (id) {
            var temp;
            ko.utils.arrayForEach(self.chatUsers(), function (user) {
                if (user.userName() == id)
                    temp = user;
            });
            self.chatUsers.remove(temp);
        }

        self.uploadFiles = function () {
            var form = document.getElementById("uploadForm");
            $.ajax({
                type: "POST",
                url: '/api/Upload',
                data: new FormData(form),
                contentType: false,
                processData: false,
                success: function () {
                    $("#UploadedFile").val("");
                },
                error: function (error) {
                    alert('Error: ' + error.responseText);
                }
            });
        }
    }

    // Represent server data
    function ChatGroup(id, name) {
        var self = this;
        self.id = ko.observable(id);
        self.name = ko.observable(name);
    }

    function ChatUser(userName, displayName, avatar, currentGroup, device) {
        var self = this;
        self.userName = ko.observable(userName);
        self.displayName = ko.observable(displayName);
        self.avatar = ko.observable(avatar);
        self.currentGroup = ko.observable(currentGroup);
        self.device = ko.observable(device);
    }

    function ChatMessage(content, timestamp, from, isMine, avatar) {
        var self = this;
        self.content = ko.observable(content);
        self.timestamp = ko.observable(timestamp);
        self.from = ko.observable(from);
        self.isMine = ko.observable(isMine);
        self.avatar = ko.observable(avatar);
    }

    var viewModel = new AppViewModel();
    ko.applyBindings(viewModel);
});
