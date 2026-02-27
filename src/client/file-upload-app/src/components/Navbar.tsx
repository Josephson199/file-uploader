import React from 'react';
import { Link } from 'react-router-dom';
import Button from '@mui/material/Button';
import AppBar from '@mui/material/AppBar';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';


interface NavBarProps {}

const NavBar: React.FC<NavBarProps> = () => {

  const handleLogin = () => {
    window.location.href = '/api/login';
  };

  const handleLogout = () => {
    window.location.href = '/api/logout';
  };

  return (
    <AppBar position="fixed">
      <Toolbar>
        <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
          <Link to="/" style={{ color: 'inherit', textDecoration: 'none' }}>
            Sandbox Application
          </Link>
        </Typography>
        
          <>
            <Button color="inherit" component={Link} to="/uploads">
              My Uploads
            </Button>
            <Button color="inherit" onClick={handleLogout}>
              Logout
            </Button>
          </>
          <Button color="inherit" onClick={handleLogin}>
            Login
          </Button>
      </Toolbar>
    </AppBar>
  );
};

export default NavBar;